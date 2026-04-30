using System;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediaTracker.Tests
{
    [Trait("Category","Integration")]
    public class IntegrationTests
    {
        private static T RequireNotNull<T>(T? value, string message) where T : class
            => value ?? throw new InvalidOperationException(message);

        private static Type? FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(name, throwOnError: false, ignoreCase: false);
                    if (t != null) return t;
                }
                catch { }
            }

            var bySimple = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            if (bySimple != null) return bySimple;

            var candidates = new[] { "MediaBrowser.Controller", "MediaBrowser.Model", "Jellyfin.Controller", "Jellyfin.Model" };
            foreach (var c in candidates)
            {
                try
                {
                    var asm = Assembly.Load(new AssemblyName(c));
                    var t = asm.GetTypes().FirstOrDefault(x => x.Name == name || x.FullName == name);
                    if (t != null) return t;
                }
                catch { }
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetTypes().FirstOrDefault(x => x.Name == name && (x.Namespace?.Contains("MediaBrowser") == true || x.Namespace?.Contains("Jellyfin") == true));
                    if (t != null) return t;
                }
                catch { }
            }

            return null;
        }

        private static Type RequireType(params string[] names)
        {
            foreach (var name in names)
            {
                var type = FindType(name) ?? Type.GetType(name, throwOnError: false, ignoreCase: false);
                if (type != null)
                {
                    return type;
                }
            }

            throw new InvalidOperationException($"Could not find any of these types: {string.Join(", ", names)}");
        }

        private static object CreateInstance(Type type, bool nonPublic = false)
            => Activator.CreateInstance(type, nonPublic)
                ?? throw new InvalidOperationException($"Could not create instance of {type.FullName}");

        private static PropertyInfo? FindProperty(Type type, params string[] names)
        {
            foreach (var name in names)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (property != null)
                {
                    return property;
                }
            }

            return null;
        }

        private static PropertyInfo RequireProperty(Type type, params string[] names)
            => FindProperty(type, names)
                ?? throw new InvalidOperationException($"Property not found on {type.FullName}: {string.Join(", ", names)}");

        private static MethodInfo RequireMethod(Type type, string name)
            => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Method not found on {type.FullName}: {name}");

        private static Jellyfin.Plugin.MediaTracker.Plugin CreateConfiguredPlugin(Guid guid)
        {
            var appPathsMock = new Mock<MediaBrowser.Common.Configuration.IApplicationPaths>();
            appPathsMock.SetupGet(x => x.PluginConfigurationsPath).Returns("/tmp");
            appPathsMock.SetupGet(x => x.PluginsPath).Returns("/tmp/plugins");
            appPathsMock.SetupGet(x => x.ProgramDataPath).Returns("/tmp/data");

            var xmlSerializerMock = new Mock<MediaBrowser.Model.Serialization.IXmlSerializer>();
            var plugin = new Jellyfin.Plugin.MediaTracker.Plugin(appPathsMock.Object, xmlSerializerMock.Object);

            var configurationProperty = RequireProperty(typeof(Jellyfin.Plugin.MediaTracker.Plugin), "Configuration");
            var configObject = CreateInstance(configurationProperty.PropertyType);
            var usersProperty = FindProperty(configurationProperty.PropertyType, "users");
            if (usersProperty != null)
            {
                var elementType = usersProperty.PropertyType.IsArray
                    ? usersProperty.PropertyType.GetElementType()
                    : usersProperty.PropertyType.GetGenericArguments().FirstOrDefault();

                if (elementType != null)
                {
                    var users = Array.CreateInstance(elementType, 1);
                    var configuredUser = CreateInstance(elementType, true);
                    FindProperty(elementType, "id", "Id")?.SetValue(configuredUser, guid.ToString());
                    FindProperty(elementType, "apiToken", "ApiToken")?.SetValue(configuredUser, "apitoken123");
                    users.SetValue(configuredUser, 0);
                    usersProperty.SetValue(configObject, users);
                }
            }

            FindProperty(configurationProperty.PropertyType, "mediaTrackerUrl", "MediaTrackerUrl")?.SetValue(configObject, "http://example.local/");
            configurationProperty.SetValue(plugin, configObject);
            return plugin;
        }

        private static Type FindUserType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var candidate = asm.GetTypes().FirstOrDefault(t =>
                        t.Name == "User"
                        && t.FullName?.StartsWith("Jellyfin.Plugin.MediaTracker", StringComparison.Ordinal) != true
                        && (t.Namespace?.Contains("MediaBrowser") == true || t.Namespace?.Contains("Jellyfin") == true));

                    if (candidate != null)
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }

            return RequireType("User");
        }

        private static object CreateConfiguredUser(Type userType, Guid guid)
        {
            var user = CreateInstance(userType);
            FindProperty(userType, "Id")?.SetValue(user, guid);
            FindProperty(userType, "Username", "Name")?.SetValue(user, "testuser");
            return user;
        }

        private static object CreateTypedList(Type elementType, object item)
        {
            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(elementType);
            var list = CreateInstance(listType);
            RequireMethod(listType, "Add").Invoke(list, new[] { item });
            return list;
        }

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

        [Fact(Skip = "Replaced by SeenNotification tests; kept for reference")]
        public async Task PluginCallsMediaTrackerOnPlayback()
        {
            // Arrange
            var sessionMock = new Mock<ISessionManager>();

            var handler = new TestHandler();
            var httpClient = new HttpClient(handler);
            var httpFactoryMock = new Mock<IHttpClientFactory>();
            httpFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

            var userManager = new Mock<IUserManager>().Object;
            var userDataManager = new Mock<MediaBrowser.Controller.Library.IUserDataManager>().Object;

            var guid = Guid.NewGuid();
            _ = CreateConfiguredPlugin(guid);

            // Create ServerEntryPoint
            var server = new Jellyfin.Plugin.MediaTracker.ServerEntryPoint(sessionMock.Object, httpFactoryMock.Object, loggerFactory, userManager, userDataManager);

            // Build a PlaybackProgressEventArgs instance via reflection
            var tPlaybackArgs = RequireType("MediaBrowser.Controller.Library.PlaybackProgressEventArgs", "PlaybackProgressEventArgs");
            var playbackArgs = CreateInstance(tPlaybackArgs);

            // Set minimal properties via reflection
            var userType = FindUserType();
            var userInstance = CreateConfiguredUser(userType, guid);
            var usersList = CreateTypedList(userType, userInstance);

            FindProperty(tPlaybackArgs, "Users", "UserIds")?.SetValue(playbackArgs, usersList);

            var movieType = RequireType("MediaBrowser.Controller.Entities.Movie", "MediaBrowser.Model.Entities.Movie", "Movie");
            var movie = CreateInstance(movieType);
            FindProperty(movieType, "Name")?.SetValue(movie, "Test Movie");
            FindProperty(movieType, "RunTimeTicks")?.SetValue(movie, (long)TimeSpan.FromMinutes(10).Ticks);

            var itemProp = RequireProperty(tPlaybackArgs, "Item");
            itemProp.SetValue(playbackArgs, movie);

            var posProp = RequireProperty(tPlaybackArgs, "PlaybackPositionTicks");
            posProp.SetValue(playbackArgs, (long)TimeSpan.FromMinutes(9).Ticks);

            var deviceProp = RequireProperty(tPlaybackArgs, "DeviceName");
            deviceProp.SetValue(playbackArgs, "TestDevice");

            // Act: raise event
            sessionMock.Raise(s => s.PlaybackProgress += null, this, playbackArgs);

            // allow async handlers to run
            await Task.Delay(200);

            // Assert: handler received a PUT request
            var lastRequest = RequireNotNull(handler.LastRequest, "Expected HTTP request was not sent.");
            var requestUri = RequireNotNull(lastRequest.RequestUri, "Expected request URI was not set.");
            var requestContent = RequireNotNull(lastRequest.Content, "Expected request content was not set.");
            Assert.Contains("/api/progress/by-external-id", requestUri.ToString());
            var content = await requestContent.ReadAsStringAsync();
            Assert.Contains("Test Movie", content);
        }

        [Fact(Skip = "Replaced by SeenNotification tests; kept for reference")]
        public async Task PluginMarksMovieAsSeenWhenProgressExceedsThreshold()
        {
            // Arrange (reuse setup from other test)
            var sessionMock = new Mock<ISessionManager>();

            var handler = new TestHandler();
            var httpClient = new HttpClient(handler);
            var httpFactoryMock = new Mock<IHttpClientFactory>();
            httpFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

            var userManager = new Mock<IUserManager>().Object;
            var userDataManager = new Mock<MediaBrowser.Controller.Library.IUserDataManager>().Object;

            var guid = Guid.NewGuid();
            _ = CreateConfiguredPlugin(guid);

            var server = new Jellyfin.Plugin.MediaTracker.ServerEntryPoint(sessionMock.Object, httpFactoryMock.Object, loggerFactory, userManager, userDataManager);

            var tPlaybackArgs = RequireType("MediaBrowser.Controller.Library.PlaybackProgressEventArgs", "PlaybackProgressEventArgs");
            var playbackArgs = CreateInstance(tPlaybackArgs);

            var userType = FindUserType();
            var userInstance = CreateConfiguredUser(userType, guid);
            var usersList = CreateTypedList(userType, userInstance);
            FindProperty(tPlaybackArgs, "Users", "UserIds")?.SetValue(playbackArgs, usersList);

            var movieType = RequireType("MediaBrowser.Controller.Entities.Movie", "MediaBrowser.Model.Entities.Movie", "Movie");
            var movie = CreateInstance(movieType);
            FindProperty(movieType, "Name")?.SetValue(movie, "Seen Movie");
            FindProperty(movieType, "Id")?.SetValue(movie, Guid.NewGuid());
            FindProperty(movieType, "RunTimeTicks")?.SetValue(movie, (long)TimeSpan.FromMinutes(10).Ticks);

            var itemProp = RequireProperty(tPlaybackArgs, "Item");
            itemProp.SetValue(playbackArgs, movie);

            var posProp = RequireProperty(tPlaybackArgs, "PlaybackPositionTicks");
            // set to 95% of runtime
            posProp.SetValue(playbackArgs, (long)TimeSpan.FromMinutes(9.5).Ticks);

            var deviceProp = RequireProperty(tPlaybackArgs, "DeviceName");
            deviceProp.SetValue(playbackArgs, "DeviceSeen");

            // Act
            sessionMock.Raise(s => s.PlaybackProgress += null, this, playbackArgs);
            await Task.Delay(200);

            // Assert last call is seen
            var lastRequest = RequireNotNull(handler.LastRequest, "Expected HTTP request was not sent.");
            var requestUri = RequireNotNull(lastRequest.RequestUri, "Expected request URI was not set.");
            var requestContent = RequireNotNull(lastRequest.Content, "Expected request content was not set.");
            Assert.Contains("/api/seen/by-external-id", requestUri.ToString());
            var content = await requestContent.ReadAsStringAsync();
            Assert.Contains("Seen Movie", content);
        }

        [Fact(Skip = "Replaced by SeenNotification tests; kept for reference")]
        public async Task PluginMarksEpisodeAsSeenWhenProgressExceedsThreshold()
        {
            var sessionMock = new Mock<ISessionManager>();

            var handler = new TestHandler();
            var httpClient = new HttpClient(handler);
            var httpFactoryMock = new Mock<IHttpClientFactory>();
            httpFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

            var userManager = new Mock<IUserManager>().Object;
            var userDataManager = new Mock<MediaBrowser.Controller.Library.IUserDataManager>().Object;

            var guid = Guid.NewGuid();
            _ = CreateConfiguredPlugin(guid);

            var server = new Jellyfin.Plugin.MediaTracker.ServerEntryPoint(sessionMock.Object, httpFactoryMock.Object, loggerFactory, userManager, userDataManager);

            var tPlaybackArgs = RequireType("MediaBrowser.Controller.Library.PlaybackProgressEventArgs", "PlaybackProgressEventArgs");
            var playbackArgs = CreateInstance(tPlaybackArgs);

            var userType = FindUserType();
            var userInstance = CreateConfiguredUser(userType, guid);
            var usersList = CreateTypedList(userType, userInstance);
            FindProperty(tPlaybackArgs, "Users", "UserIds")?.SetValue(playbackArgs, usersList);

            var episodeType = RequireType("MediaBrowser.Controller.Entities.TV.Episode", "MediaBrowser.Model.Entities.Episode", "Episode");
            var episode = CreateInstance(episodeType);
            FindProperty(episodeType, "Id")?.SetValue(episode, Guid.NewGuid());
            FindProperty(episodeType, "IndexNumber")?.SetValue(episode, 1);

            // Series
            var seriesType = RequireType("MediaBrowser.Controller.Entities.TV.Series", "MediaBrowser.Model.Entities.Series", "Series");
            var series = CreateInstance(seriesType);
            // set provider ids dictionary
            var provIdsProp = FindProperty(seriesType, "ProviderIds");
            if (provIdsProp != null)
            {
                var dictType = typeof(System.Collections.Generic.Dictionary<string, string>);
                var dict = CreateInstance(dictType) as IDictionary;
                if (dict == null)
                {
                    throw new InvalidOperationException("Could not create provider id dictionary.");
                }

                dict["Imdb"] = "tt1234567";
                provIdsProp.SetValue(series, dict);
            }

            // Season
            var seasonType = RequireType("MediaBrowser.Controller.Entities.TV.Season", "MediaBrowser.Model.Entities.Season", "Season");
            var season = CreateInstance(seasonType);
            FindProperty(seasonType, "IndexNumber")?.SetValue(season, 1);

            // attach series and season
            var seriesProp = FindProperty(episodeType, "Series");
            seriesProp?.SetValue(episode, series);
            var seasonProp = FindProperty(episodeType, "Season");
            seasonProp?.SetValue(episode, season);

            // assign Item
            var itemProp = RequireProperty(tPlaybackArgs, "Item");
            itemProp.SetValue(playbackArgs, episode);

            // runtime and position
            FindProperty(episodeType, "RunTimeTicks")?.SetValue(episode, (long)TimeSpan.FromMinutes(20).Ticks);
            var posProp = RequireProperty(tPlaybackArgs, "PlaybackPositionTicks");
            posProp.SetValue(playbackArgs, (long)TimeSpan.FromMinutes(19).Ticks);

            // Act
            sessionMock.Raise(s => s.PlaybackProgress += null, this, playbackArgs);
            await Task.Delay(200);

            // Assert
            var lastRequest = RequireNotNull(handler.LastRequest, "Expected HTTP request was not sent.");
            var requestUri = RequireNotNull(lastRequest.RequestUri, "Expected request URI was not set.");
            var requestContent = RequireNotNull(lastRequest.Content, "Expected request content was not set.");
            Assert.Contains("/api/seen/by-external-id", requestUri.ToString());
            var content = await requestContent.ReadAsStringAsync();
            Assert.Contains("tt1234567", content);
        }
    }
}
