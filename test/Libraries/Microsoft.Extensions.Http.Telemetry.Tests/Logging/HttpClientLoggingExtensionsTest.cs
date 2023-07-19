// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Testing;
using Microsoft.Extensions.Http.Telemetry.Logging.Test.Internal;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Telemetry.Testing.Logging;
using Moq;
using Xunit;

namespace Microsoft.Extensions.Http.Telemetry.Logging.Test;

public class EnrichableLoggingExtensionsTest
{
    private readonly Fixture _fixture;

    public EnrichableLoggingExtensionsTest()
    {
        _fixture = new Fixture();
    }

    [Fact]
    public void AddEnrichableLogging_AnyArgumentIsNull_Throws()
    {
        var act = () => ((IHttpClientBuilder)null!).AddEnrichableLogging();
        act.Should().Throw<ArgumentNullException>();

        act = () => ((IHttpClientBuilder)null!).AddEnrichableLogging(_ => { });
        act.Should().Throw<ArgumentNullException>();

        act = () => ((IHttpClientBuilder)null!).AddEnrichableLogging(Mock.Of<IConfigurationSection>());
        act.Should().Throw<ArgumentNullException>();

        act = () => Mock.Of<IHttpClientBuilder>().AddEnrichableLogging((Action<LoggingOptions>)null!);
        act.Should().Throw<ArgumentNullException>();

        act = () => Mock.Of<IHttpClientBuilder>().AddEnrichableLogging((IConfigurationSection)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddHttpClientLogEnricher_AnyArgumentIsNull_Throws()
    {
        var act = () => ((IServiceCollection)null!).AddHttpClientLogEnricher<EmptyEnricher>();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddEnrichableLogging_ConfiguredOptionsWithNamedClient_ShouldNotBeSame()
    {
        var services = new ServiceCollection();

        var provider = services
            .AddHttpClient("test1")
            .AddEnrichableLogging(options => options.BodyReadTimeout = TimeSpan.FromSeconds(1))
            .Services
            .AddHttpClient("test2")
            .AddEnrichableLogging(options => options.BodyReadTimeout = TimeSpan.FromSeconds(2))
            .Services
            .BuildServiceProvider();

        var optionsFirst = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test1");
        var optionsSecond = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test2");
        optionsFirst.Should().NotBeNull();
        optionsSecond.Should().NotBeNull();
        optionsFirst.Should().NotBeEquivalentTo(optionsSecond);
        optionsFirst.BodyReadTimeout.Should().Be(TimeSpan.FromSeconds(1));
        optionsSecond.BodyReadTimeout.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AddEnrichableLogging_ConfiguredOptionsWithTypedClient_ShouldNotBeSame()
    {
        var services = new ServiceCollection();

        var provider = services
            .AddHttpClient<ITestHttpClient1, TestHttpClient1>()
            .AddEnrichableLogging(options => options.BodyReadTimeout = TimeSpan.FromSeconds(1))
            .Services
            .AddHttpClient<ITestHttpClient2, TestHttpClient2>()
            .AddEnrichableLogging(options => options.BodyReadTimeout = TimeSpan.FromSeconds(2))
            .Services
            .BuildServiceProvider();

        var optionsFirst = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(nameof(ITestHttpClient1));
        var optionsSecond = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(nameof(ITestHttpClient2));
        optionsFirst.Should().NotBeNull();
        optionsSecond.Should().NotBeNull();
        optionsFirst.Should().NotBeEquivalentTo(optionsSecond);
        optionsFirst.BodyReadTimeout.Should().Be(TimeSpan.FromSeconds(1));
        optionsSecond.BodyReadTimeout.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AddEnrichableLogging_DefaultOptions_CreatesOptionsCorrectly()
    {
        var services = new ServiceCollection();

        var provider = services
            .AddHttpClient("")
            .AddEnrichableLogging(o => o.RequestHeadersDataClasses.Add("test1", SimpleClassifications.PrivateData))
            .Services
            .AddHttpClient("")
            .AddEnrichableLogging(o => o.RequestHeadersDataClasses.Add("test2", SimpleClassifications.PrivateData))
            .Services
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<LoggingOptions>>().Value;
        options.RequestHeadersDataClasses.Should().HaveCount(2);
        options.RequestHeadersDataClasses.Should().ContainKeys(new List<string> { "test1", "test2" });
        options.RequestHeadersDataClasses.Should().ContainValues(new List<DataClassification> { SimpleClassifications.PrivateData });
    }

    [Fact]
    public void AddEnrichableLogging_GivenActionDelegate_RegistersInDi()
    {
        var requestBodyContentType = "application/json";
        var responseBodyContentType = "application/json";
        var requestHeader = _fixture.Create<string>();
        var responseHeader = _fixture.Create<string>();
        var bodyReadTimeout = TimeSpan.FromSeconds(1);
        var bodySizeLimit = 100;
        var formatRequestPath = _fixture.Create<OutgoingPathLoggingMode>();
        var formatRequestPathParameters = _fixture.Create<HttpRouteParameterRedactionMode>();
        var logStart = _fixture.Create<bool>();
        var paramToRedact = new KeyValuePair<string, DataClassification>("userId", SimpleClassifications.PrivateData);

        var services = new ServiceCollection();

        services
            .AddHttpClient("test")
            .AddEnrichableLogging(options =>
            {
                options.RequestBodyContentTypes.Add(requestBodyContentType);
                options.ResponseBodyContentTypes.Add(responseBodyContentType);
                options.BodyReadTimeout = bodyReadTimeout;
                options.BodySizeLimit = bodySizeLimit;
                options.RequestPathLoggingMode = formatRequestPath;
                options.RequestPathParameterRedactionMode = formatRequestPathParameters;
                options.RequestHeadersDataClasses.Add(requestHeader, SimpleClassifications.PrivateData);
                options.ResponseHeadersDataClasses.Add(responseHeader, SimpleClassifications.PrivateData);
                options.RouteParameterDataClasses.Add(paramToRedact);
                options.LogRequestStart = logStart;
            });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test");

        options.Should().NotBeNull();
        options.RequestBodyContentTypes.Should().ContainSingle();
        options.RequestBodyContentTypes.Should().Contain(requestBodyContentType);
        options.ResponseBodyContentTypes.Should().ContainSingle();
        options.ResponseBodyContentTypes.Should().Contain(responseBodyContentType);
        options.BodyReadTimeout.Should().Be(bodyReadTimeout);
        options.BodySizeLimit.Should().Be(bodySizeLimit);
        options.RequestPathLoggingMode.Should().Be(formatRequestPath);
        options.RequestPathParameterRedactionMode.Should().Be(formatRequestPathParameters);
        options.RequestHeadersDataClasses.Should().ContainSingle();
        options.RequestHeadersDataClasses.Should().Contain(requestHeader, SimpleClassifications.PrivateData);
        options.ResponseHeadersDataClasses.Should().ContainSingle();
        options.ResponseHeadersDataClasses.Should().Contain(responseHeader, SimpleClassifications.PrivateData);
        options.RouteParameterDataClasses.Should().ContainSingle();
        options.RouteParameterDataClasses.Should().Contain(paramToRedact);
        options.LogRequestStart.Should().Be(logStart);
    }

    [Fact]
    public async Task AddEnrichableLogging_GivenInvalidOptions_Throws()
    {
        using var host = FakeHost.CreateBuilder()
            .ConfigureServices(services =>
            {
                services
                    .AddFakeRedaction()
                    .AddHttpClient("test")
                    .AddEnrichableLogging(options =>
                    {
                        options.BodyReadTimeout = TimeSpan.Zero;
                        options.BodySizeLimit = -1;
                    });
            })
            .Build();

        var act = async () => await host.StartAsync().ConfigureAwait(false);
        await act.Should().ThrowAsync<OptionsValidationException>().ConfigureAwait(false);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(59)]
    [InlineData(17)]
    public void AddEnrichableLogging_GivenConfigurationSection_SetsTimeoutCorrectly(int seconds)
    {
        var timeoutValue = TimeSpan.FromSeconds(seconds);

        using var provider = new ServiceCollection()
            .AddHttpClient("test")
            .AddEnrichableLogging(TestConfiguration.GetHttpClientLoggingConfigurationSection(timeoutValue))
            .Services
            .BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test");

        options.Should().NotBeNull();
        options.BodyReadTimeout.Should().Be(timeoutValue);
    }

    [Fact]
    public void AddHttpClientLogEnricher_RegistersEnricherInDI()
    {
        using var provider = new ServiceCollection()
            .AddHttpClientLogEnricher<EmptyEnricher>()
            .BuildServiceProvider();

        var enricherRegistered = provider.GetService<IHttpClientLogEnricher>();

        enricherRegistered.Should().NotBeNull();
        enricherRegistered.Should().BeOfType<EmptyEnricher>();
    }

    [Fact]
    public void AddEnrichableLogging_ServiceCollection_GivenActionDelegate_RegistersInDi()
    {
        var requestBodyContentType = "application/json";
        var responseBodyContentType = "application/json";
        var requestHeader = _fixture.Create<string>();
        var responseHeader = _fixture.Create<string>();
        var bodyReadTimeout = TimeSpan.FromSeconds(1);
        var bodySizeLimit = 100;
        var formatRequestPath = _fixture.Create<OutgoingPathLoggingMode>();
        var formatRequestPathParameters = _fixture.Create<HttpRouteParameterRedactionMode>();
        var logStart = _fixture.Create<bool>();
        var paramToRedact = new KeyValuePair<string, DataClassification>("userId", SimpleClassifications.PrivateData);

        var services = new ServiceCollection();

        services
            .AddFakeRedaction()
            .AddHttpClient()
            .ConfigureHttpClientDefaults(b => b.AddEnrichableLogging(options =>
            {
                options.RequestBodyContentTypes.Add(requestBodyContentType);
                options.ResponseBodyContentTypes.Add(responseBodyContentType);
                options.BodyReadTimeout = bodyReadTimeout;
                options.BodySizeLimit = bodySizeLimit;
                options.RequestPathLoggingMode = formatRequestPath;
                options.RequestPathParameterRedactionMode = formatRequestPathParameters;
                options.RequestHeadersDataClasses.Add(requestHeader, SimpleClassifications.PrivateData);
                options.ResponseHeadersDataClasses.Add(responseHeader, SimpleClassifications.PrivateData);
                options.RouteParameterDataClasses.Add(paramToRedact);
                options.LogRequestStart = logStart;
            }));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LoggingOptions>>().Value;

        options.Should().NotBeNull();
        options.RequestBodyContentTypes.Should().ContainSingle();
        options.RequestBodyContentTypes.Should().Contain(requestBodyContentType);
        options.ResponseBodyContentTypes.Should().ContainSingle();
        options.ResponseBodyContentTypes.Should().Contain(responseBodyContentType);
        options.BodyReadTimeout.Should().Be(bodyReadTimeout);
        options.BodySizeLimit.Should().Be(bodySizeLimit);
        options.RequestPathLoggingMode.Should().Be(formatRequestPath);
        options.RequestPathParameterRedactionMode.Should().Be(formatRequestPathParameters);
        options.RequestHeadersDataClasses.Should().ContainSingle();
        options.RequestHeadersDataClasses.Should().Contain(requestHeader, SimpleClassifications.PrivateData);
        options.ResponseHeadersDataClasses.Should().ContainSingle();
        options.ResponseHeadersDataClasses.Should().Contain(responseHeader, SimpleClassifications.PrivateData);
        options.RouteParameterDataClasses.Should().ContainSingle();
        options.RouteParameterDataClasses.Should().Contain(paramToRedact);
        options.LogRequestStart.Should().Be(logStart);

        using var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
        Assert.NotNull(httpClient);
    }

    [Fact]
    public async Task AddEnrichableLogging_ServiceCollection_GivenInvalidOptions_Throws()
    {
        var provider = new ServiceCollection()
            .AddFakeRedaction()
            .AddHttpClient()
            .ConfigureHttpClientDefaults(b => b.AddEnrichableLogging(options =>
            {
                options.BodyReadTimeout = TimeSpan.Zero;
                options.BodySizeLimit = -1;
            }))
            .BuildServiceProvider();

        var act = () =>
            provider
                .GetRequiredService<IHostedService>()
                .StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<OptionsValidationException>().ConfigureAwait(false);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(59)]
    [InlineData(17)]
    public void AddEnrichableLogging_ServiceCollection_GivenConfigurationSection_SetsTimeoutCorrectly(int seconds)
    {
        var timeoutValue = TimeSpan.FromSeconds(seconds);

        using var provider = new ServiceCollection()
            .AddFakeRedaction()
            .AddHttpClient()
            .ConfigureHttpClientDefaults(b => b.AddEnrichableLogging(TestConfiguration.GetHttpClientLoggingConfigurationSection(timeoutValue)))
            .BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<LoggingOptions>>().Value;

        options.Should().NotBeNull();
        options.BodyReadTimeout.Should().Be(timeoutValue);

        using var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
        Assert.NotNull(httpClient);
    }

    [Fact]
    public void AddEnrichableLogging_ServiceCollection_CreatesClientSuccessfully()
    {
        using var sp = new ServiceCollection()
            .AddFakeRedaction()
            .AddHttpClient()
            .ConfigureHttpClientDefaults(b => b.AddEnrichableLogging())
            .BuildServiceProvider();

        using var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
        Assert.NotNull(httpClient);
    }

    [Fact]
    public async Task AddEnrichableLogging_ServiceCollectionAndEnrichers_EnrichesLogsWithAllEnrichers()
    {
        const string RequestPath = "https://we.wont.hit.this.dd22anyway.com";

        await using var sp = new ServiceCollection()
            .AddFakeLogging()
            .AddFakeRedaction()
            .ConfigureHttpClientDefaults(b => b.AddEnrichableLogging())
            .AddHttpClientLogEnricher<EnricherWithCounter>()
            .AddHttpClientLogEnricher<TestEnricher>()
            .AddHttpClient("testClient").Services
            .BlockRemoteCall()
            .BuildServiceProvider();

        using var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("testClient");
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(RequestPath),
        };

        _ = await httpClient.SendAsync(httpRequestMessage).ConfigureAwait(false);
        var collector = sp.GetFakeLogCollector();
        var logRecord = collector.GetSnapshot().Single(logRecord => logRecord.Category == "Microsoft.Extensions.Http.Telemetry.Logging.Internal.EnrichableHttpClientLogger");

        Assert.Empty(logRecord.Message);
        var state = logRecord.StructuredState;
        var enricher1 = sp.GetServices<IHttpClientLogEnricher>().SingleOrDefault(enn => enn is EnricherWithCounter) as EnricherWithCounter;
        var enricher2 = sp.GetServices<IHttpClientLogEnricher>().SingleOrDefault(enn => enn is TestEnricher) as TestEnricher;

        enricher1.Should().NotBeNull();
        enricher2.Should().NotBeNull();

        enricher1!.TimesCalled.Should().Be(1);
        state!.Single(kvp => kvp.Key == enricher2!.KvpRequest.Key).Value.Should().Be(enricher2!.KvpRequest.Value!.ToString());
    }

    [Fact]
    public async Task AddEnrichableLogging_WithNamedHttpClients_WorksCorrectly()
    {
        const string RequestPath = "https://we.wont.hit.this.dd22anyway.com";

        await using var provider = new ServiceCollection()
             .AddFakeLogging()
             .AddFakeRedaction()
             .AddHttpClient("namedClient1")
             .AddEnrichableLogging(o =>
             {
                 o.ResponseHeadersDataClasses.Add("ResponseHeader", SimpleClassifications.PrivateData);
                 o.RequestHeadersDataClasses.Add("RequestHeader", SimpleClassifications.PrivateData);
                 o.RequestHeadersDataClasses.Add("RequestHeaderFirst", SimpleClassifications.PrivateData);
                 o.RequestBodyContentTypes.Add("application/json");
                 o.ResponseBodyContentTypes.Add("application/json");
                 o.LogBody = true;
             }).Services
             .AddHttpClient("namedClient2")
             .AddEnrichableLogging(o =>
             {
                 o.ResponseHeadersDataClasses.Add("ResponseHeader", SimpleClassifications.PrivateData);
                 o.RequestHeadersDataClasses.Add("RequestHeader", SimpleClassifications.PrivateData);
                 o.RequestHeadersDataClasses.Add("RequestHeaderSecond", SimpleClassifications.PrivateData);
                 o.RequestBodyContentTypes.Add("application/json");
                 o.ResponseBodyContentTypes.Add("application/json");
                 o.LogBody = true;
             }).Services
             .BlockRemoteCall()
             .BuildServiceProvider();

        using var namedClient1 = provider.GetRequiredService<IHttpClientFactory>().CreateClient("namedClient1");
        using var namedClient2 = provider.GetRequiredService<IHttpClientFactory>().CreateClient("namedClient2");

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(RequestPath),
        };
        httpRequestMessage.Headers.Add("requestHeader", "Request Value");
        httpRequestMessage.Headers.Add("ReQuEStHeAdErFirst", new List<string> { "Request Value 2", "Request Value 3" });
        var responseString = await SendRequest(namedClient1, httpRequestMessage);
        var collector = provider.GetFakeLogCollector();
        var logRecord = collector.GetSnapshot().Single(l => l.Category == "Microsoft.Extensions.Http.Telemetry.Logging.Internal.EnrichableHttpClientLogger");
        var state = logRecord.State as List<KeyValuePair<string, string>>;
        state.Should().Contain(kvp => kvp.Value == responseString);
        state.Should().Contain(kvp => kvp.Value == "Request Value");
        state.Should().Contain(kvp => kvp.Value == "Request Value 2,Request Value 3");

        using var httpRequestMessage2 = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(RequestPath),
        };
        httpRequestMessage2.Headers.Add("requestHeader", "Request Value");
        httpRequestMessage2.Headers.Add("ReQuEStHeAdErSecond", new List<string> { "Request Value 2", "Request Value 3" });
        collector.Clear();
        responseString = await SendRequest(namedClient2, httpRequestMessage2);
        logRecord = collector.GetSnapshot().Single(l => l.Category == "Microsoft.Extensions.Http.Telemetry.Logging.Internal.EnrichableHttpClientLogger");
        state = logRecord.State as List<KeyValuePair<string, string>>;
        state.Should().Contain(kvp => kvp.Value == responseString);
        state.Should().Contain(kvp => kvp.Value == "Request Value");
        state.Should().Contain(kvp => kvp.Value == "Request Value 2,Request Value 3");
    }

    private static async Task<string> SendRequest(System.Net.Http.HttpClient httpClient, HttpRequestMessage httpRequestMessage)
    {
        var content = await httpClient
            .SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        var responseStream = await content.Content.ReadAsStreamAsync();
        var buffer = new byte[32768];
        _ = await responseStream.ReadAsync(buffer, 0, 32768);
        return Encoding.UTF8.GetString(buffer);
    }

    [Fact]
    public async Task AddEnrichableLogging_WithTypedHttpClients_WorksCorrectly()
    {
        const string RequestPath = "https://we.wont.hit.this.dd22anyway.com";

        await using var provider = new ServiceCollection()
            .AddFakeLogging()
            .AddFakeRedaction()
            .AddSingleton<ITestHttpClient1, TestHttpClient1>()
            .AddSingleton<ITestHttpClient2, TestHttpClient2>()
            .AddHttpClient<ITestHttpClient1, TestHttpClient1>()
            .AddEnrichableLogging(x =>
            {
                x.ResponseHeadersDataClasses.Add("ResponseHeader", SimpleClassifications.PrivateData);
                x.RequestHeadersDataClasses.Add("RequestHeader", SimpleClassifications.PrivateData);
                x.RequestHeadersDataClasses.Add("RequestHeader2", SimpleClassifications.PrivateData);
                x.RequestBodyContentTypes.Add("application/json");
                x.ResponseBodyContentTypes.Add("application/json");
                x.BodySizeLimit = 10000;
                x.LogBody = true;
            }).Services
            .AddHttpClient<ITestHttpClient2, TestHttpClient2>()
            .AddEnrichableLogging(x =>
            {
                x.ResponseHeadersDataClasses.Add("ResponseHeader", SimpleClassifications.PrivateData);
                x.RequestHeadersDataClasses.Add("RequestHeader", SimpleClassifications.PrivateData);
                x.RequestHeadersDataClasses.Add("RequestHeader2", SimpleClassifications.PrivateData);
                x.RequestBodyContentTypes.Add("application/json");
                x.ResponseBodyContentTypes.Add("application/json");
                x.BodySizeLimit = 20000;
                x.LogBody = true;
            }).Services
            .BlockRemoteCall()
            .BuildServiceProvider();

        var firstClient = provider.GetService<ITestHttpClient1>() as TestHttpClient1;
        var secondClient = provider.GetService<ITestHttpClient2>() as TestHttpClient2;

        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(RequestPath),
        };
        httpRequestMessage.Headers.Add("requestHeader", "Request Value");
        httpRequestMessage.Headers.Add("ReQuEStHeAdEr2", new List<string> { "Request Value 2", "Request Value 3" });
        var content = await firstClient!.SendRequest(httpRequestMessage).ConfigureAwait(false);
        var collector = provider.GetFakeLogCollector();
        var responseStream = await content.Content.ReadAsStreamAsync();
        var buffer = new byte[10000];
        _ = await responseStream.ReadAsync(buffer, 0, 10000);
        var responseString = Encoding.UTF8.GetString(buffer);

        var logRecord = collector.GetSnapshot().Single(l => l.Category == "Microsoft.Extensions.Http.Telemetry.Logging.Internal.EnrichableHttpClientLogger");
        var state = logRecord.State as List<KeyValuePair<string, string>>;
        state.Should().Contain(kvp => kvp.Value == responseString);
        state.Should().Contain(kvp => kvp.Value == "Request Value");
        state.Should().Contain(kvp => kvp.Value == "Request Value 2,Request Value 3");

        using var httpRequestMessage2 = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(RequestPath),
        };
        httpRequestMessage2.Headers.Add("requestHeader", "Request Value");
        httpRequestMessage2.Headers.Add("ReQuEStHeAdEr2", new List<string> { "Request Value 2", "Request Value 3" });
        collector.Clear();
        content = await secondClient!.SendRequest(httpRequestMessage2).ConfigureAwait(false);
        responseStream = await content.Content.ReadAsStreamAsync();
        buffer = new byte[20000];
        _ = await responseStream.ReadAsync(buffer, 0, 20000);
        responseString = Encoding.UTF8.GetString(buffer);

        logRecord = collector.GetSnapshot().Single(l => l.Category == "Microsoft.Extensions.Http.Telemetry.Logging.Internal.EnrichableHttpClientLogger");
        state = logRecord.State as List<KeyValuePair<string, string>>;
        state.Should().Contain(kvp => kvp.Value == responseString);
        state.Should().Contain(kvp => kvp.Value == "Request Value");
        state.Should().Contain(kvp => kvp.Value == "Request Value 2,Request Value 3");
    }

    [Theory]
    [InlineData(HttpRouteParameterRedactionMode.Strict, "v1/unit/REDACTED/users/REDACTED:123")]
    [InlineData(HttpRouteParameterRedactionMode.Loose, "v1/unit/999/users/REDACTED:123")]
    [InlineData(HttpRouteParameterRedactionMode.None, "/v1/unit/999/users/123")]
    public async Task AddEnrichableLogging_RedactSensitivePrams(HttpRouteParameterRedactionMode parameterRedactionMode, string redactedPath)
    {
        const string RequestPath = "https://fake.com/v1/unit/999/users/123";

        await using var sp = new ServiceCollection()
            .AddFakeLogging()
            .AddFakeRedaction(o => o.RedactionFormat = "REDACTED:{0}")
            .AddHttpClient()
            .ConfigureHttpClientDefaults(b => b.AddEnrichableLogging(o =>
            {
                o.RouteParameterDataClasses.Add("userId", SimpleClassifications.PrivateData);
                o.RequestPathParameterRedactionMode = parameterRedactionMode;
            }))
            .BlockRemoteCall()
            .BuildServiceProvider();

        using var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(RequestPath),
        };

        var requestContext = sp.GetRequiredService<IOutgoingRequestContext>();
        requestContext.SetRequestMetadata(new RequestMetadata
        {
            RequestRoute = "/v1/unit/{unitId}/users/{userId}"
        });

        _ = await httpClient.SendAsync(httpRequestMessage).ConfigureAwait(false);

        var collector = sp.GetFakeLogCollector();
        var logRecord = collector.GetSnapshot().Single(logRecord => logRecord.Category == "Microsoft.Extensions.Http.Telemetry.Logging.Internal.EnrichableHttpClientLogger");
        var state = logRecord.State as List<KeyValuePair<string, string>>;
        state!.Single(kvp => kvp.Key == "httpPath").Value.Should().Be(redactedPath);
    }

    [Theory]
    [InlineData(HttpRouteParameterRedactionMode.Strict, "v1/unit/REDACTED/users/REDACTED:123")]
    [InlineData(HttpRouteParameterRedactionMode.Loose, "v1/unit/999/users/REDACTED:123")]
    public async Task AddEnrichableLogging_NamedHttpClient_RedactSensitivePrams(HttpRouteParameterRedactionMode parameterRedactionMode, string redactedPath)
    {
        const string RequestPath = "https://fake.com/v1/unit/999/users/123";

        await using var sp = new ServiceCollection()
            .AddFakeLogging()
            .AddFakeRedaction(o => o.RedactionFormat = "REDACTED:{0}")
            .AddHttpClient("test")
            .AddEnrichableLogging(o =>
            {
                o.RouteParameterDataClasses.Add("userId", SimpleClassifications.PrivateData);
                o.RequestPathParameterRedactionMode = parameterRedactionMode;
            })
            .Services
            .BlockRemoteCall()
            .BuildServiceProvider();

        using var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(RequestPath),
        };

        var requestContext = sp.GetRequiredService<IOutgoingRequestContext>();
        requestContext.SetRequestMetadata(new RequestMetadata
        {
            RequestRoute = "/v1/unit/{unitId}/users/{userId}"
        });

        _ = await httpClient.SendAsync(httpRequestMessage).ConfigureAwait(false);

        var collector = sp.GetFakeLogCollector();
        var logRecord = collector.GetSnapshot().Single(logRecord => logRecord.Category == "Microsoft.Extensions.Http.Telemetry.Logging.Internal.EnrichableHttpClientLogger");
        var state = logRecord.State as List<KeyValuePair<string, string>>;
        state!.Single(kvp => kvp.Key == "httpPath").Value.Should().Be(redactedPath);
    }

    [Fact]
    public void AddEnrichableLogging_WithNamedClients_RegistersNamedOptions()
    {
        const string FirstClientName = "1";
        const string SecondClientName = "2";

        using var provider = new ServiceCollection()
            .AddFakeRedaction()
            .AddHttpClient(FirstClientName)
            .AddEnrichableLogging(options =>
            {
                options.LogRequestStart = true;
                options.ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { "test1", SimpleClassifications.PrivateData } };
            })
            .Services
            .AddHttpClient(SecondClientName)
            .AddEnrichableLogging(options =>
            {
                options.LogRequestStart = false;
                options.ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { "test2", SimpleClassifications.PrivateData } };
            })
            .Services
            .BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var firstClient = factory.CreateClient(FirstClientName);
        var secondClient = factory.CreateClient(SecondClientName);
        firstClient.Should().NotBe(secondClient);

        var optionsFirst = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(FirstClientName);
        var optionsSecond = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(SecondClientName);
        optionsFirst.Should().NotBeNull();
        optionsSecond.Should().NotBeNull();
        optionsFirst.Should().NotBeEquivalentTo(optionsSecond);
    }

    [Fact]
    public void AddEnrichableLogging_WithTypedClients_RegistersNamedOptions()
    {
        using var provider = new ServiceCollection()
            .AddFakeRedaction()
            .AddSingleton<ITestHttpClient1, TestHttpClient1>()
            .AddSingleton<ITestHttpClient2, TestHttpClient2>()
            .AddHttpClient<ITestHttpClient1, TestHttpClient1>()
            .AddEnrichableLogging(options =>
            {
                options.LogRequestStart = true;
                options.ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { "test1", SimpleClassifications.PrivateData } };
            })
            .Services
            .AddHttpClient<ITestHttpClient2, TestHttpClient2>()
            .AddEnrichableLogging(options =>
            {
                options.LogRequestStart = false;
                options.ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { "test2", SimpleClassifications.PrivateData } };
            })
            .Services
            .BuildServiceProvider();

        var firstClient = provider.GetService<ITestHttpClient1>() as TestHttpClient1;
        var secondClient = provider.GetService<ITestHttpClient2>() as TestHttpClient2;

        firstClient.Should().NotBe(secondClient);

        var optionsFirst = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(nameof(ITestHttpClient1));
        var optionsSecond = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(nameof(ITestHttpClient2));
        optionsFirst.Should().NotBeNull();
        optionsSecond.Should().NotBeNull();
        optionsFirst.Should().NotBeEquivalentTo(optionsSecond);
    }

    [Fact]
    public void AddEnrichableLogging_WithTypedAndNamedClients_RegistersNamedOptions()
    {
        using var provider = new ServiceCollection()
            .AddFakeRedaction()
            .AddSingleton<ITestHttpClient1, TestHttpClient1>()
            .AddSingleton<ITestHttpClient2, TestHttpClient2>()
            .AddHttpClient<ITestHttpClient1, TestHttpClient1>()
            .AddEnrichableLogging(options =>
            {
                options.ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { "test1", SimpleClassifications.PrivateData } };
            })
            .Services
            .AddHttpClient<ITestHttpClient2, TestHttpClient2>()
            .AddEnrichableLogging(options =>
            {
                options.ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { "test2", SimpleClassifications.PrivateData } };
            })
            .Services
            .AddHttpClient("testClient3")
            .AddEnrichableLogging(options =>
            {
                options.ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { "test3", SimpleClassifications.PrivateData } };
            })
            .Services
            .AddHttpClient("testClient4")
            .AddEnrichableLogging(options =>
            {
                options.ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { "test4", SimpleClassifications.PrivateData } };
            })
            .Services
            .AddHttpClient<ITestHttpClient1, TestHttpClient1>("testClient5")
            .AddEnrichableLogging(options =>
            {
                options.ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { "test5", SimpleClassifications.PrivateData } };
            })
            .Services
            .ConfigureHttpClientDefaults(b => b.AddEnrichableLogging(options =>
            {
                options.ResponseHeadersDataClasses = new Dictionary<string, DataClassification> { { "test6", SimpleClassifications.PrivateData } };
            }))
            .BuildServiceProvider();

        var optionsFirst = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(nameof(ITestHttpClient1));
        var optionsSecond = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(nameof(ITestHttpClient2));
        var optionsThird = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("testClient3");
        var optionsFourth = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("testClient4");
        var optionsFifth = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("testClient5");
        var optionsSixth = provider.GetRequiredService<IOptions<LoggingOptions>>().Value;

        optionsFirst.Should().NotBeNull();
        optionsSecond.Should().NotBeNull();
        optionsFirst.Should().NotBeEquivalentTo(optionsSecond);

        optionsThird.Should().NotBeNull();
        optionsFourth.Should().NotBeNull();
        optionsThird.Should().NotBeEquivalentTo(optionsFourth);

        optionsFifth.Should().NotBeNull();
        optionsFifth.Should().NotBeEquivalentTo(optionsFourth);

        optionsSixth.Should().NotBeNull();
        optionsSixth.Should().NotBeEquivalentTo(optionsFifth);
    }

    [Fact]
    public async Task AddEnrichableLogging_DisablesNetScope()
    {
        const string RequestPath = "https://we.wont.hit.this.dd22anyway.com";
        await using var provider = new ServiceCollection()
             .AddFakeLogging()
             .AddFakeRedaction()
             .AddHttpClient("test")
             .AddEnrichableLogging()
             .Services
             .BlockRemoteCall()
             .BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test");
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri(RequestPath));

        _ = await client.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        var collector = provider.GetFakeLogCollector();
        var logRecord = collector.GetSnapshot().Single(l => l.Category == "Microsoft.Extensions.Http.Telemetry.Logging.Internal.EnrichableHttpClientLogger");

        logRecord.Scopes.Should().HaveCount(0);
    }

    [Fact]
    public async Task AddEnrichableLogging_CallFromOtherClient_HasBuiltInLogging()
    {
        const string RequestPath = "https://we.wont.hit.this.dd22anyway.com";
        await using var provider = new ServiceCollection()
             .AddFakeLogging()
             .AddFakeRedaction()
             .AddHttpClient("test")
             .AddEnrichableLogging()
             .Services
             .AddHttpClient("normal")
             .Services
             .BlockRemoteCall()
             .BuildServiceProvider();

        // The test client has AddEnrichableLogging. The normal client doesn't.
        // The normal client should still log via the builtin HTTP logging.
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("normal");
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri(RequestPath));

        _ = await client.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        var collector = provider.GetFakeLogCollector();
        var logRecords = collector.GetSnapshot().Where(l => l.Category == "System.Net.Http.HttpClient.normal.LogicalHandler").ToList();

        Assert.Collection(logRecords,
            r => Assert.Equal("RequestPipelineStart", r.Id.Name),
            r => Assert.Equal("RequestPipelineEnd", r.Id.Name));
    }

    [Fact]
    public async Task AddDefaultEnrichableLogging_DisablesNetScope()
    {
        const string RequestPath = "https://we.wont.hit.this.dd22anyway.com";
        await using var provider = new ServiceCollection()
             .AddFakeLogging()
             .AddFakeRedaction()
             .AddHttpClient()
             .ConfigureHttpClientDefaults(b => b.AddEnrichableLogging())
             .BlockRemoteCall()
             .BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get("test");
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri(RequestPath));

        _ = await client.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        var collector = provider.GetFakeLogCollector();
        var logRecord = collector.GetSnapshot().Single(l => l.Category == "Microsoft.Extensions.Http.Telemetry.Logging.Internal.EnrichableHttpClientLogger");

        logRecord.Scopes.Should().HaveCount(0);
    }
}
