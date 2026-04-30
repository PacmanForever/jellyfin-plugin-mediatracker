using System;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaTracker.Tests;

[Trait("Category","Integration")]
public class SeenNotificationTests
{
    private static T RequireNotNull<T>(T? value, string message) where T : class
        => value ?? throw new InvalidOperationException(message);

    private static PropertyInfo RequireProperty(Type type, params string[] names)
    {
        foreach (var name in names)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (property != null)
            {
                return property;
            }
        }

        throw new InvalidOperationException($"Property not found on {type.FullName}: {string.Join(", ", names)}");
    }

    private static MethodInfo RequireMethod(Type type, string name)
        => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method not found on {type.FullName}: {name}");

    private static object CreateInstance(Type type, bool nonPublic = false)
        => Activator.CreateInstance(type, nonPublic)
            ?? throw new InvalidOperationException($"Could not create instance of {type.FullName}");

    class TestHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            LastRequest = request;
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") };
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task MarkAsSeen_SendsSeenEndpoint_ForMovie()
    {
        var handler = new TestHandler();
        var http = new HttpClient(handler);
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(http);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var userManager = new Mock<MediaBrowser.Controller.Library.IUserManager>().Object;
        var userDataManager = new Mock<MediaBrowser.Controller.Library.IUserDataManager>().Object;
        var sessionManager = new Mock<MediaBrowser.Controller.Session.ISessionManager>().Object;

        var appPathsMock = new Mock<MediaBrowser.Common.Configuration.IApplicationPaths>();
        appPathsMock.SetupGet(x => x.PluginConfigurationsPath).Returns("/tmp");
        appPathsMock.SetupGet(x => x.PluginsPath).Returns("/tmp/plugins");
        appPathsMock.SetupGet(x => x.ProgramDataPath).Returns("/tmp/data");
        var xmlSerializerMock = new Mock<MediaBrowser.Model.Serialization.IXmlSerializer>();
        var plugin = new Jellyfin.Plugin.MediaTracker.Plugin(appPathsMock.Object, xmlSerializerMock.Object);

        // configure runtime-compatible configuration
        var confProp = RequireProperty(typeof(Jellyfin.Plugin.MediaTracker.Plugin), "Configuration");
        var configObj = CreateInstance(confProp.PropertyType);
        var guid = Guid.NewGuid();
        var usersPropRuntime = confProp.PropertyType.GetProperty("users");
        if (usersPropRuntime != null)
        {
            var elemType = usersPropRuntime.PropertyType.IsArray ? usersPropRuntime.PropertyType.GetElementType() : usersPropRuntime.PropertyType.GetGenericArguments().FirstOrDefault();
            if (elemType != null)
            {
                var arr = Array.CreateInstance(elemType, 1);
                var userInstanceRuntime = CreateInstance(elemType, true);
                var idPropRt = elemType.GetProperty("id") ?? elemType.GetProperty("Id");
                var apiPropRt = elemType.GetProperty("apiToken") ?? elemType.GetProperty("ApiToken");
                idPropRt?.SetValue(userInstanceRuntime, guid.ToString());
                apiPropRt?.SetValue(userInstanceRuntime, "apitoken123");
                arr.SetValue(userInstanceRuntime, 0);
                usersPropRuntime.SetValue(configObj, arr);
            }
        }
        var urlProp = confProp.PropertyType.GetProperty("mediaTrackerUrl") ?? confProp.PropertyType.GetProperty("MediaTrackerUrl");
        urlProp?.SetValue(configObj, "https://example.local/");
        confProp.SetValue(plugin, configObj);

        // create server
        var server = new Jellyfin.Plugin.MediaTracker.ServerEntryPoint(sessionManager, httpFactory.Object, loggerFactory, userManager, userDataManager);

        // Prepare a dynamic user with Guid id
        dynamic user = new ExpandoObject();
        user.Id = guid;
        user.Username = "testuser";

        // Prepare payload like MarkAsSeen would
        dynamic payload = new ExpandoObject();
        payload.mediaType = "movie";
        payload.id = new { imdbId = "tt123", tmdbId = (long?)123 };
        payload.duration = 600000;

        // Invoke private MarkAsSeen via reflection
        var mi = RequireMethod(typeof(Jellyfin.Plugin.MediaTracker.ServerEntryPoint), "MarkAsSeen");
        var task = RequireNotNull(mi.Invoke(server, new object[] { user, payload }) as Task, "MarkAsSeen did not return a Task.");
        await task;

        var lastRequest = RequireNotNull(handler.LastRequest, "Expected HTTP request was not sent.");
        var requestUri = RequireNotNull(lastRequest.RequestUri, "Expected request URI was not set.");
        var requestContent = RequireNotNull(lastRequest.Content, "Expected request content was not set.");
        Assert.Contains("/api/seen/by-external-id", requestUri.ToString());
        Assert.DoesNotContain("token=", requestUri.Query);
        Assert.Equal("Bearer", lastRequest.Headers.Authorization?.Scheme);
        Assert.True(lastRequest.Headers.Contains("X-Api-Token"));
        var content = await requestContent.ReadAsStringAsync();
        Assert.Contains("tt123", content);
    }

    [Fact]
    public async Task MarkAsSeen_SendsSeenEndpoint_ForEpisode()
    {
        var handler = new TestHandler();
        var http = new HttpClient(handler);
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(http);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var userManager = new Mock<MediaBrowser.Controller.Library.IUserManager>().Object;
        var userDataManager = new Mock<MediaBrowser.Controller.Library.IUserDataManager>().Object;
        var sessionManager = new Mock<MediaBrowser.Controller.Session.ISessionManager>().Object;

        var appPathsMock = new Mock<MediaBrowser.Common.Configuration.IApplicationPaths>();
        appPathsMock.SetupGet(x => x.PluginConfigurationsPath).Returns("/tmp");
        appPathsMock.SetupGet(x => x.PluginsPath).Returns("/tmp/plugins");
        appPathsMock.SetupGet(x => x.ProgramDataPath).Returns("/tmp/data");
        var xmlSerializerMock = new Mock<MediaBrowser.Model.Serialization.IXmlSerializer>();
        var plugin = new Jellyfin.Plugin.MediaTracker.Plugin(appPathsMock.Object, xmlSerializerMock.Object);

        // configure runtime-compatible configuration
        var confProp = RequireProperty(typeof(Jellyfin.Plugin.MediaTracker.Plugin), "Configuration");
        var configObj = CreateInstance(confProp.PropertyType);
        var guid = Guid.NewGuid();
        var usersPropRuntime = confProp.PropertyType.GetProperty("users");
        if (usersPropRuntime != null)
        {
            var elemType = usersPropRuntime.PropertyType.IsArray ? usersPropRuntime.PropertyType.GetElementType() : usersPropRuntime.PropertyType.GetGenericArguments().FirstOrDefault();
            if (elemType != null)
            {
                var arr = Array.CreateInstance(elemType, 1);
                var userInstanceRuntime = CreateInstance(elemType, true);
                var idPropRt = elemType.GetProperty("id") ?? elemType.GetProperty("Id");
                var apiPropRt = elemType.GetProperty("apiToken") ?? elemType.GetProperty("ApiToken");
                idPropRt?.SetValue(userInstanceRuntime, guid.ToString());
                apiPropRt?.SetValue(userInstanceRuntime, "apitoken123");
                arr.SetValue(userInstanceRuntime, 0);
                usersPropRuntime.SetValue(configObj, arr);
            }
        }
        var urlProp = confProp.PropertyType.GetProperty("mediaTrackerUrl") ?? confProp.PropertyType.GetProperty("MediaTrackerUrl");
        urlProp?.SetValue(configObj, "https://example.local/");
        confProp.SetValue(plugin, configObj);

        var server = new Jellyfin.Plugin.MediaTracker.ServerEntryPoint(sessionManager, httpFactory.Object, loggerFactory, userManager, userDataManager);

        dynamic user = new ExpandoObject();
        user.Id = guid;
        user.Username = "testuser";

        dynamic payload = new ExpandoObject();
        payload.mediaType = "tv";
        payload.id = new { imdbId = "tt999", tmdbId = (long?)999 };
        payload.seasonNumber = 1;
        payload.episodeNumber = 2;
        payload.duration = 1200000;

        var mi = RequireMethod(typeof(Jellyfin.Plugin.MediaTracker.ServerEntryPoint), "MarkAsSeen");
        var task = RequireNotNull(mi.Invoke(server, new object[] { user, payload }) as Task, "MarkAsSeen did not return a Task.");
        await task;

        var lastRequest = RequireNotNull(handler.LastRequest, "Expected HTTP request was not sent.");
        var requestUri = RequireNotNull(lastRequest.RequestUri, "Expected request URI was not set.");
        var requestContent = RequireNotNull(lastRequest.Content, "Expected request content was not set.");
        Assert.Contains("/api/seen/by-external-id", requestUri.ToString());
        Assert.DoesNotContain("token=", requestUri.Query);
        Assert.Equal("Bearer", lastRequest.Headers.Authorization?.Scheme);
        Assert.True(lastRequest.Headers.Contains("X-Api-Token"));
        var content = await requestContent.ReadAsStringAsync();
        Assert.Contains("tt999", content);
    }
}
