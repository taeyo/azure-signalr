// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Infrastructure;
using Microsoft.AspNet.SignalR.Messaging;
using Microsoft.AspNet.SignalR.Transports;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Options;
using Microsoft.Owin.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Owin;
using Xunit;

namespace Microsoft.Azure.SignalR.AspNet.Tests
{
    public class RunAzureSignalRTests
    {
        private const string ServiceUrl = "http://localhost:8086";
        private const string ConnectionString = "Endpoint=http://localhost;AccessKey=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789;";
        private const string ConnectionString2 = "Endpoint=http://localhost2;AccessKey=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789;";
        private const string AppName = "AzureSignalRTest";

        [Fact]
        public void TestRunAzureSignalRWithDefaultOptions()
        {
            var hubConfig = new HubConfiguration();
            using (WebApp.Start(ServiceUrl, app => app.RunAzureSignalR(AppName, ConnectionString, hubConfig)))
            {
                var resolver = hubConfig.Resolver;
                var options = resolver.Resolve<IOptions<ServiceOptions>>();
                Assert.Equal(ConnectionString, options.Value.ConnectionString);
                Assert.IsType<ServiceHubDispatcher>(resolver.Resolve<PersistentConnection>());
                Assert.IsType<ServiceEndpointProvider>(resolver.Resolve<IServiceEndpointProvider>());
                Assert.IsType<ServiceConnectionManager>(resolver.Resolve<IServiceConnectionManager>());
                Assert.IsType<EmptyProtectedData>(resolver.Resolve<IProtectedData>());
                Assert.IsType<ServiceMessageBus>(resolver.Resolve<IMessageBus>());
                Assert.IsType<AzureTransportManager>(resolver.Resolve<ITransportManager>());
                Assert.IsType<ServiceProtocol>(resolver.Resolve<IServiceProtocol>());
            }
        }

        [Fact]
        public void TestRunAzureSignalRWithoutConnectionString()
        {
            var exception = Assert.Throws<ArgumentException>(
                () =>
                {
                    using (WebApp.Start(ServiceUrl, app => app.RunAzureSignalR(AppName)))
                    {
                    }
                });
            Assert.StartsWith("No connection string was specified.", exception.Message);
        }

        [Fact]
        public void TestRunAzureSignalRWithConnectionString()
        {
            var hubConfig = new HubConfiguration();
            using (WebApp.Start(ServiceUrl, app => app.RunAzureSignalR(AppName, ConnectionString, hubConfig)))
            {
                var options = hubConfig.Resolver.Resolve<IOptions<ServiceOptions>>();
                Assert.Equal(ConnectionString, options.Value.ConnectionString);
            }
        }

        [Fact]
        public void TestRunAzureSignalRWithConfig()
        {
            // Prepare the configuration
            using (new AppSettingsConfigScope(ConnectionString))
            using (new ConnectionStringConfigScope(ConnectionString2))
            {
                var hubConfig = new HubConfiguration();
                using (WebApp.Start(ServiceUrl, app => app.RunAzureSignalR(AppName, hubConfig)))
                {
                    var options = hubConfig.Resolver.Resolve<IOptions<ServiceOptions>>();

                    // The one in ConnectionString wins
                    Assert.Equal(ConnectionString2, options.Value.ConnectionString);
                }
            }
        }

        [Fact]
        public void TestRunAzureSignalRWithAppSettings()
        {
            // Prepare the configuration
            using (new AppSettingsConfigScope(ConnectionString))
            {
                var hubConfig = new HubConfiguration();
                using (WebApp.Start(ServiceUrl, app => app.RunAzureSignalR(AppName, hubConfig)))
                {
                    var options = hubConfig.Resolver.Resolve<IOptions<ServiceOptions>>();

                    Assert.Equal(ConnectionString, options.Value.ConnectionString);
                }
            }
        }

        [Fact]
        public void TestRunAzureSignalRWithOptionsContainDefaultValue()
        {
            using (new ConnectionStringConfigScope(ConnectionString2))
            {
                var hubConfig = new HubConfiguration();
                using (WebApp.Start(ServiceUrl, app => app.RunAzureSignalR(AppName, hubConfig, o =>
                {
                    o.ConnectionCount = -1;
                })))
                {
                    var options = hubConfig.Resolver.Resolve<IOptions<ServiceOptions>>();
                    Assert.Equal(ConnectionString2, options.Value.ConnectionString);
                    Assert.Equal(-1, options.Value.ConnectionCount);
                }
            }
        }

        [Fact]
        public void TestRunAzureSignalRWithOptions()
        {
            var hubConfig = new HubConfiguration();
            using (WebApp.Start(ServiceUrl, app => app.RunAzureSignalR(AppName, hubConfig, o =>
            {
                o.ConnectionString = ConnectionString;
                o.ConnectionCount = -1;
            })))
            {
                var options = hubConfig.Resolver.Resolve<IOptions<ServiceOptions>>();
                Assert.Equal(ConnectionString, options.Value.ConnectionString);
                Assert.Equal(-1, options.Value.ConnectionCount);
            }
        }

        [Theory]
        [InlineData(typeof(NullUserIdProvider), null)]
        [InlineData(typeof(CustomUserIdProvider), "hello")]
        public async Task TestNegotiateWithRunAzureSignalR(Type providerType, string expectedUser)
        {
            var hubConfiguration = new HubConfiguration();
            hubConfiguration.Resolver.Register(typeof(IUserIdProvider), () => Activator.CreateInstance(providerType));
            using (WebApp.Start(ServiceUrl, a => a.RunAzureSignalR(AppName, ConnectionString, hubConfiguration)))
            {
                var client = new HttpClient { BaseAddress = new Uri(ServiceUrl) };
                var response = await client.GetAsync("/negotiate");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var message = await response.Content.ReadAsStringAsync();
                var responseObject = JsonConvert.DeserializeObject<ResponseMessage>(message);
                Assert.Equal("2.0", responseObject.ProtocolVersion);
                Assert.Equal("http://localhost/aspnetclient", responseObject.RedirectUrl);
                Assert.NotNull(responseObject.AccessToken);
                var token = JwtSecurityTokenHandler.ReadJwtToken(responseObject.AccessToken);
                Assert.Equal(AppName, token.Claims.FirstOrDefault(s => s.Type == Constants.ClaimType.AppName).Value);
                var user = token.Claims.FirstOrDefault(s => s.Type == Constants.ClaimType.UserId)?.Value;
                Assert.Equal(expectedUser, user);
            }
        }

        [Fact]
        public async Task TestClaimsProviderInServiceOptionsTakeEffect()
        {
            var hubConfiguration = new HubConfiguration();
            using (WebApp.Start(ServiceUrl, a => a.RunAzureSignalR(AppName, hubConfiguration, options =>
            {
                options.ConnectionString = ConnectionString;
                options.ClaimsProvider = context => new Claim[]
                {
                    new Claim("user", "hello"),
                };
            })))
            {
                var client = new HttpClient { BaseAddress = new Uri(ServiceUrl) };
                var response = await client.GetAsync("/negotiate");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var message = await response.Content.ReadAsStringAsync();
                var responseObject = JsonConvert.DeserializeObject<ResponseMessage>(message);
                Assert.Equal("2.0", responseObject.ProtocolVersion);
                Assert.Equal("http://localhost/aspnetclient", responseObject.RedirectUrl);
                Assert.NotNull(responseObject.AccessToken);
                var token = JwtSecurityTokenHandler.ReadJwtToken(responseObject.AccessToken);
                Assert.Equal(AppName, token.Claims.FirstOrDefault(s => s.Type == Constants.ClaimType.AppName).Value);
                var user = token.Claims.FirstOrDefault(s => s.Type == "user")?.Value;
                Assert.Equal("hello", user);
                var requestId = token.Claims.FirstOrDefault(s => s.Type == Constants.ClaimType.Id);
                Assert.NotNull(requestId);
            }
        }

        private sealed class AppSettingsConfigScope : IDisposable
        {
            private readonly string _originalSetting;

            public  AppSettingsConfigScope(string setting)
            {
                _originalSetting = ConfigurationManager.AppSettings[ServiceOptions.ConnectionStringDefaultKey];
                ConfigurationManager.AppSettings[ServiceOptions.ConnectionStringDefaultKey] = setting;
            }

            public void Dispose()
            {
                ConfigurationManager.AppSettings[ServiceOptions.ConnectionStringDefaultKey] = _originalSetting;
            }
        }

        private sealed class ConnectionStringConfigScope : IDisposable
        {
            private readonly string _originalConnectionString;

            public ConnectionStringConfigScope(string connectionString)
            {
                _originalConnectionString = ConfigurationManager.ConnectionStrings[ServiceOptions.ConnectionStringDefaultKey]?.ConnectionString;
                SetConnectionStringConfig(connectionString);
            }

            public void Dispose()
            {
                SetConnectionStringConfig(_originalConnectionString);
            }

            private void SetConnectionStringConfig(string connectionString)
            {
                var settings = ConfigurationManager.ConnectionStrings;

                var element = typeof(ConfigurationElement).GetField("_bReadOnly", BindingFlags.Instance | BindingFlags.NonPublic);
                var collection = typeof(ConfigurationElementCollection).GetField("bReadOnly", BindingFlags.Instance | BindingFlags.NonPublic);

                element.SetValue(settings, false);
                collection.SetValue(settings, false);
                if (connectionString == null)
                {
                    settings.Remove(ServiceOptions.ConnectionStringDefaultKey);
                }
                else
                {
                    settings.Add(new ConnectionStringSettings(ServiceOptions.ConnectionStringDefaultKey, connectionString));
                }

                collection.SetValue(settings, true);
                element.SetValue(settings, true);
            }
        }

        private sealed class NullUserIdProvider : IUserIdProvider
        {
            public string GetUserId(IRequest request)
            {
                return null;
            }
        }

        private sealed class CustomUserIdProvider : IUserIdProvider
        {
            public string GetUserId(IRequest request)
            {
                return "hello";
            }
        }

        private static readonly JwtSecurityTokenHandler JwtSecurityTokenHandler = new JwtSecurityTokenHandler();

        private sealed class ResponseMessage
        {
            public string ProtocolVersion { get; set; }

            public string RedirectUrl { get; set; }

            public string AccessToken { get; set; }
        }
    }
}