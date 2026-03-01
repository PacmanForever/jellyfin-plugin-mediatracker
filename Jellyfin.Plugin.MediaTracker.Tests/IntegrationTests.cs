using System;
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
        static Type? FindType(string name)
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

            var userManager = new Mock<MediaBrowser.Controller.Library.IUserManager>().Object;
            var userDataManager = new Mock<MediaBrowser.Controller.Library.IUserDataManager>().Object;

            // Create Plugin instance and set configuration via reflection
            var appPathsMock = new Mock<MediaBrowser.Common.Configuration.IApplicationPaths>();
            appPathsMock.SetupGet(x => x.PluginConfigurationsPath).Returns("/tmp");
            appPathsMock.SetupGet(x => x.PluginsPath).Returns("/tmp/plugins");
            appPathsMock.SetupGet(x => x.ProgramDataPath).Returns("/tmp/data");
            var xmlSerializerMock = new Mock<MediaBrowser.Model.Serialization.IXmlSerializer>();
            var plugin = new Jellyfin.Plugin.MediaTracker.Plugin(appPathsMock.Object, xmlSerializerMock.Object);
            var guid = Guid.NewGuid();
            // build configuration instance compatible with runtime Property type
            var confProp = typeof(Jellyfin.Plugin.MediaTracker.Plugin).GetProperty("Configuration", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var configObj = Activator.CreateInstance(confProp.PropertyType);
            var usersPropRuntime = confProp.PropertyType.GetProperty("users");
            if (usersPropRuntime != null)
            {
                var elemType = usersPropRuntime.PropertyType.IsArray ? usersPropRuntime.PropertyType.GetElementType() : usersPropRuntime.PropertyType.GetGenericArguments().FirstOrDefault();
                if (elemType != null)
                {
                    var arr = Array.CreateInstance(elemType, 1);
                    var userInstanceRuntime = Activator.CreateInstance(elemType);
                    var idPropRt = elemType.GetProperty("id") ?? elemType.GetProperty("Id");
                    var apiPropRt = elemType.GetProperty("apiToken") ?? elemType.GetProperty("ApiToken");
                    idPropRt?.SetValue(userInstanceRuntime, guid.ToString());
                    apiPropRt?.SetValue(userInstanceRuntime, "apitoken123");
                    arr.SetValue(userInstanceRuntime, 0);
                    usersPropRuntime.SetValue(configObj, arr);
                }
            }
            var urlProp = confProp.PropertyType.GetProperty("mediaTrackerUrl") ?? confProp.PropertyType.GetProperty("MediaTrackerUrl");
            urlProp?.SetValue(configObj, "http://example.local/");
            confProp.SetValue(plugin, configObj);

            // Create ServerEntryPoint
            var server = new Jellyfin.Plugin.MediaTracker.ServerEntryPoint(sessionMock.Object, httpFactoryMock.Object, loggerFactory, userManager, userDataManager);

            // Build a PlaybackProgressEventArgs instance via reflection
            var tPlaybackArgs = FindType("MediaBrowser.Controller.Library.PlaybackProgressEventArgs") ?? FindType("PlaybackProgressEventArgs");
            Assert.NotNull(tPlaybackArgs);
            var playbackArgs = Activator.CreateInstance(tPlaybackArgs!);

            // Set minimal properties via reflection
            Type? userType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var candidate = asm.GetTypes().FirstOrDefault(t => t.Name == "User" && !t.FullName.StartsWith("Jellyfin.Plugin.MediaTracker", StringComparison.Ordinal));
                    if (candidate != null && (candidate.Namespace?.Contains("MediaBrowser") == true || candidate.Namespace?.Contains("Jellyfin") == true))
                    {
                        userType = candidate;
                        break;
                    }
                }
                catch { }
            }
            userType ??= FindType("User");
            Assert.NotNull(userType);
            var userInstance = Activator.CreateInstance(userType);
            var idProp = userType.GetProperty("Id");
            if (idProp != null) idProp.SetValue(userInstance, guid);
            var usernameProp = userType.GetProperty("Username") ?? userType.GetProperty("Name");
            if (usernameProp != null) usernameProp.SetValue(userInstance, "testuser");

            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(userType);
            var usersList = Activator.CreateInstance(listType);
            var addMethod = listType.GetMethod("Add");
            addMethod.Invoke(usersList, new[] { userInstance });

            var usersProp = tPlaybackArgs.GetProperty("Users") ?? tPlaybackArgs.GetProperty("UserIds");
            if (usersProp != null) usersProp.SetValue(playbackArgs, usersList);

            var movieType = FindType("MediaBrowser.Controller.Entities.Movie") ?? FindType("MediaBrowser.Model.Entities.Movie") ?? FindType("Movie");
            Assert.NotNull(movieType);
            var movie = Activator.CreateInstance(movieType);
            var nameProp = movieType.GetProperty("Name");
            nameProp?.SetValue(movie, "Test Movie");
            var runTimeProp = movieType.GetProperty("RunTimeTicks");
            runTimeProp?.SetValue(movie, (long)TimeSpan.FromMinutes(10).Ticks);

            var itemProp = tPlaybackArgs.GetProperty("Item");
            itemProp.SetValue(playbackArgs, movie);

            var posProp = tPlaybackArgs.GetProperty("PlaybackPositionTicks");
            posProp.SetValue(playbackArgs, (long)TimeSpan.FromMinutes(9).Ticks);

            var deviceProp = tPlaybackArgs.GetProperty("DeviceName");
            deviceProp.SetValue(playbackArgs, "TestDevice");

            // Act: raise event
            sessionMock.Raise(s => s.PlaybackProgress += null, this, playbackArgs);

            // allow async handlers to run
            await Task.Delay(200);

            // Assert: handler received a PUT request
            Assert.NotNull(handler.LastRequest);
            Assert.Contains("/api/progress/by-external-id", handler.LastRequest.RequestUri.ToString());
            var content = await handler.LastRequest.Content.ReadAsStringAsync();
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

            var userManager = new Mock<MediaBrowser.Controller.Library.IUserManager>().Object;
            var userDataManager = new Mock<MediaBrowser.Controller.Library.IUserDataManager>().Object;

            var appPathsMock = new Mock<MediaBrowser.Common.Configuration.IApplicationPaths>();
            appPathsMock.SetupGet(x => x.PluginConfigurationsPath).Returns("/tmp");
            appPathsMock.SetupGet(x => x.PluginsPath).Returns("/tmp/plugins");
            appPathsMock.SetupGet(x => x.ProgramDataPath).Returns("/tmp/data");
            var xmlSerializerMock = new Mock<MediaBrowser.Model.Serialization.IXmlSerializer>();
            var plugin = new Jellyfin.Plugin.MediaTracker.Plugin(appPathsMock.Object, xmlSerializerMock.Object);
            var guid = Guid.NewGuid();
            var confProp = typeof(Jellyfin.Plugin.MediaTracker.Plugin).GetProperty("Configuration", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var configObj = Activator.CreateInstance(confProp.PropertyType);
            var usersPropRuntime = confProp.PropertyType.GetProperty("users");
            if (usersPropRuntime != null)
            {
                var elemType = usersPropRuntime.PropertyType.IsArray ? usersPropRuntime.PropertyType.GetElementType() : usersPropRuntime.PropertyType.GetGenericArguments().FirstOrDefault();
                if (elemType != null)
                {
                    var arr = Array.CreateInstance(elemType, 1);
                    var userInstanceRuntime = Activator.CreateInstance(elemType);
                    var idPropRt = elemType.GetProperty("id") ?? elemType.GetProperty("Id");
                    var apiPropRt = elemType.GetProperty("apiToken") ?? elemType.GetProperty("ApiToken");
                    idPropRt?.SetValue(userInstanceRuntime, guid.ToString());
                    apiPropRt?.SetValue(userInstanceRuntime, "apitoken123");
                    arr.SetValue(userInstanceRuntime, 0);
                    usersPropRuntime.SetValue(configObj, arr);
                }
            }
            var urlProp = confProp.PropertyType.GetProperty("mediaTrackerUrl") ?? confProp.PropertyType.GetProperty("MediaTrackerUrl");
            urlProp?.SetValue(configObj, "http://example.local/");
            confProp.SetValue(plugin, configObj);

            var server = new Jellyfin.Plugin.MediaTracker.ServerEntryPoint(sessionMock.Object, httpFactoryMock.Object, loggerFactory, userManager, userDataManager);

            var tPlaybackArgs = FindType("MediaBrowser.Controller.Library.PlaybackProgressEventArgs") ?? FindType("PlaybackProgressEventArgs");
            Assert.NotNull(tPlaybackArgs);
            var playbackArgs = Activator.CreateInstance(tPlaybackArgs!);

            Type? userType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var candidate = asm.GetTypes().FirstOrDefault(t => t.Name == "User" && !t.FullName.StartsWith("Jellyfin.Plugin.MediaTracker", StringComparison.Ordinal));
                    if (candidate != null && (candidate.Namespace?.Contains("MediaBrowser") == true || candidate.Namespace?.Contains("Jellyfin") == true))
                    {
                        userType = candidate;
                        break;
                    }
                }
                catch { }
            }
            userType ??= FindType("User");
            Assert.NotNull(userType);
            var userInstance = Activator.CreateInstance(userType);
            var idProp = userType.GetProperty("Id");
            if (idProp != null) idProp.SetValue(userInstance, guid);
            var usernameProp = userType.GetProperty("Username") ?? userType.GetProperty("Name");
            if (usernameProp != null) usernameProp.SetValue(userInstance, "testuser");

            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(userType);
            var usersList = Activator.CreateInstance(listType);
            var addMethod = listType.GetMethod("Add");
            addMethod.Invoke(usersList, new[] { userInstance });
            var usersProp = tPlaybackArgs.GetProperty("Users") ?? tPlaybackArgs.GetProperty("UserIds");
            if (usersProp != null) usersProp.SetValue(playbackArgs, usersList);

            var movieType = FindType("MediaBrowser.Controller.Entities.Movie") ?? FindType("MediaBrowser.Model.Entities.Movie") ?? FindType("Movie");
            Assert.NotNull(movieType);
            var movie = Activator.CreateInstance(movieType);
            var nameProp = movieType.GetProperty("Name");
            nameProp?.SetValue(movie, "Seen Movie");
            var idMovieProp = movieType.GetProperty("Id");
            if (idMovieProp != null) idMovieProp.SetValue(movie, Guid.NewGuid());
            var runTimeProp = movieType.GetProperty("RunTimeTicks");
            runTimeProp?.SetValue(movie, (long)TimeSpan.FromMinutes(10).Ticks);

            var itemProp = tPlaybackArgs.GetProperty("Item");
            itemProp.SetValue(playbackArgs, movie);

            var posProp = tPlaybackArgs.GetProperty("PlaybackPositionTicks");
            // set to 95% of runtime
            posProp.SetValue(playbackArgs, (long)TimeSpan.FromMinutes(9.5).Ticks);

            var deviceProp = tPlaybackArgs.GetProperty("DeviceName");
            deviceProp.SetValue(playbackArgs, "DeviceSeen");

            // Act
            sessionMock.Raise(s => s.PlaybackProgress += null, this, playbackArgs);
            await Task.Delay(200);

            // Assert last call is seen
            Assert.NotNull(handler.LastRequest);
            Assert.Contains("/api/seen/by-external-id", handler.LastRequest.RequestUri.ToString());
            var content = await handler.LastRequest.Content.ReadAsStringAsync();
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

            var userManager = new Mock<MediaBrowser.Controller.Library.IUserManager>().Object;
            var userDataManager = new Mock<MediaBrowser.Controller.Library.IUserDataManager>().Object;

            var appPathsMock = new Mock<MediaBrowser.Common.Configuration.IApplicationPaths>();
            appPathsMock.SetupGet(x => x.PluginConfigurationsPath).Returns("/tmp");
            appPathsMock.SetupGet(x => x.PluginsPath).Returns("/tmp/plugins");
            appPathsMock.SetupGet(x => x.ProgramDataPath).Returns("/tmp/data");
            var xmlSerializerMock = new Mock<MediaBrowser.Model.Serialization.IXmlSerializer>();
            var plugin = new Jellyfin.Plugin.MediaTracker.Plugin(appPathsMock.Object, xmlSerializerMock.Object);
            var guid = Guid.NewGuid();
            var confProp = typeof(Jellyfin.Plugin.MediaTracker.Plugin).GetProperty("Configuration", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var configObj = Activator.CreateInstance(confProp.PropertyType);
            var usersPropRuntime = confProp.PropertyType.GetProperty("users");
            if (usersPropRuntime != null)
            {
                var elemType = usersPropRuntime.PropertyType.IsArray ? usersPropRuntime.PropertyType.GetElementType() : usersPropRuntime.PropertyType.GetGenericArguments().FirstOrDefault();
                if (elemType != null)
                {
                    var arr = Array.CreateInstance(elemType, 1);
                    var userInstanceRuntime = Activator.CreateInstance(elemType);
                    var idPropRt = elemType.GetProperty("id") ?? elemType.GetProperty("Id");
                    var apiPropRt = elemType.GetProperty("apiToken") ?? elemType.GetProperty("ApiToken");
                    idPropRt?.SetValue(userInstanceRuntime, guid.ToString());
                    apiPropRt?.SetValue(userInstanceRuntime, "apitoken123");
                    arr.SetValue(userInstanceRuntime, 0);
                    usersPropRuntime.SetValue(configObj, arr);
                }
            }
            var urlProp = confProp.PropertyType.GetProperty("mediaTrackerUrl") ?? confProp.PropertyType.GetProperty("MediaTrackerUrl");
            urlProp?.SetValue(configObj, "http://example.local/");
            confProp.SetValue(plugin, configObj);

            var server = new Jellyfin.Plugin.MediaTracker.ServerEntryPoint(sessionMock.Object, httpFactoryMock.Object, loggerFactory, userManager, userDataManager);

            var tPlaybackArgs = FindType("MediaBrowser.Controller.Library.PlaybackProgressEventArgs") ?? FindType("PlaybackProgressEventArgs");
            Assert.NotNull(tPlaybackArgs);
            var playbackArgs = Activator.CreateInstance(tPlaybackArgs!);

            Type? userType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var candidate = asm.GetTypes().FirstOrDefault(t => t.Name == "User" && !t.FullName.StartsWith("Jellyfin.Plugin.MediaTracker", StringComparison.Ordinal));
                    if (candidate != null && (candidate.Namespace?.Contains("MediaBrowser") == true || candidate.Namespace?.Contains("Jellyfin") == true))
                    {
                        userType = candidate;
                        break;
                    }
                }
                catch { }
            }
            userType ??= FindType("User");
            Assert.NotNull(userType);
            var userInstance = Activator.CreateInstance(userType);
            var idProp = userType.GetProperty("Id");
            if (idProp != null) idProp.SetValue(userInstance, guid);
            var usernameProp = userType.GetProperty("Username") ?? userType.GetProperty("Name");
            if (usernameProp != null) usernameProp.SetValue(userInstance, "testuser");

            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(userType);
            var usersList = Activator.CreateInstance(listType);
            var addMethod = listType.GetMethod("Add");
            addMethod.Invoke(usersList, new[] { userInstance });
            var usersProp = tPlaybackArgs.GetProperty("Users") ?? tPlaybackArgs.GetProperty("UserIds");
            if (usersProp != null) usersProp.SetValue(playbackArgs, usersList);

            var episodeType = FindType("MediaBrowser.Controller.Entities.TV.Episode") ?? FindType("MediaBrowser.Model.Entities.Episode") ?? FindType("Episode");
            Assert.NotNull(episodeType);
            var episode = Activator.CreateInstance(episodeType);
            var episodeIdProp = episodeType.GetProperty("Id");
            if (episodeIdProp != null) episodeIdProp.SetValue(episode, Guid.NewGuid());
            var episodeIdxProp = episodeType.GetProperty("IndexNumber");
            episodeIdxProp?.SetValue(episode, 1);

            // Series
            var seriesType = Type.GetType("MediaBrowser.Controller.Entities.TV.Series, MediaBrowser.Controller") ?? Type.GetType("MediaBrowser.Model.Entities.Series, MediaBrowser.Model");
            Assert.NotNull(seriesType);
            var series = Activator.CreateInstance(seriesType);
            // set provider ids dictionary
            var provIdsProp = seriesType.GetProperty("ProviderIds");
            if (provIdsProp != null)
            {
                var dictType = typeof(System.Collections.Generic.Dictionary<string, string>);
                var dict = Activator.CreateInstance(dictType) as System.Collections.IDictionary;
                dict["Imdb"] = "tt1234567";
                provIdsProp.SetValue(series, dict);
            }

            // Season
            var seasonType = Type.GetType("MediaBrowser.Controller.Entities.TV.Season, MediaBrowser.Controller") ?? Type.GetType("MediaBrowser.Model.Entities.Season, MediaBrowser.Model");
            var season = Activator.CreateInstance(seasonType);
            var seasonIdxProp = seasonType.GetProperty("IndexNumber");
            seasonIdxProp?.SetValue(season, 1);

            // attach series and season
            var seriesProp = episodeType.GetProperty("Series");
            seriesProp?.SetValue(episode, series);
            var seasonProp = episodeType.GetProperty("Season");
            seasonProp?.SetValue(episode, season);

            // assign Item
            var itemProp = tPlaybackArgs.GetProperty("Item");
            itemProp.SetValue(playbackArgs, episode);

            // runtime and position
            var runTimeProp = episodeType.GetProperty("RunTimeTicks");
            runTimeProp?.SetValue(episode, (long)TimeSpan.FromMinutes(20).Ticks);
            var posProp = tPlaybackArgs.GetProperty("PlaybackPositionTicks");
            posProp.SetValue(playbackArgs, (long)TimeSpan.FromMinutes(19).Ticks);

            // Act
            sessionMock.Raise(s => s.PlaybackProgress += null, this, playbackArgs);
            await Task.Delay(200);

            // Assert
            Assert.NotNull(handler.LastRequest);
            Assert.Contains("/api/seen/by-external-id", handler.LastRequest.RequestUri.ToString());
            var content = await handler.LastRequest.Content.ReadAsStringAsync();
            Assert.Contains("tt1234567", content);
        }
    }
}
