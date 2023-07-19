// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Http.Telemetry.Logging.Internal;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Options.Validation;
using Microsoft.Extensions.Telemetry.Internal;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Http.Telemetry.Logging;

/// <summary>
/// Extension methods to register HTTP client logging feature.
/// </summary>
public static class HttpClientLoggingExtensions
{
    private static IHttpClientBuilder AddEnrichableLoggingCore(IHttpClientBuilder builder)
    {
        _ = builder.Services
            .AddHttpRouteProcessor()
            .AddHttpHeadersRedactor()
            .AddOutgoingRequestContext();

        builder.Services.TryAddActivatedSingleton<IHttpRequestReader, HttpRequestReader>();
        builder.Services.TryAddActivatedSingleton<IHttpHeadersReader, HttpHeadersReader>();

        return builder.RemoveAllLoggers()
            .AddLogger(serviceProvider => {
                var loggingOptions = Microsoft.Extensions.Options.Options.Create(serviceProvider
                    .GetRequiredService<IOptionsMonitor<LoggingOptions>>().Get(builder.Name));

                return ActivatorUtilities.CreateInstance<EnrichableHttpClientLogger>(
                    serviceProvider,
                    ActivatorUtilities.CreateInstance<HttpRequestReader>(
                        serviceProvider,
                        ActivatorUtilities.CreateInstance<HttpHeadersReader>(
                            serviceProvider,
                            loggingOptions),
                        loggingOptions),
                    loggingOptions);
            },
            wrapHandlersPipeline: true); // should be considered
    }

    /// <summary>
    /// Registers HTTP client logging components into <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder" />.</param>
    /// <returns>
    /// An <see cref="IHttpClientBuilder" /> that can be used to configure the client.
    /// </returns>
    /// <exception cref="ArgumentNullException">Argument <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IHttpClientBuilder AddEnrichableLogging(this IHttpClientBuilder builder)
    {
        _ = Throw.IfNull(builder);

        _ = builder.Services
            .AddValidatedOptions<LoggingOptions, LoggingOptionsValidator>(builder.Name);

        AddEnrichableLoggingCore(builder);

        return AddEnrichableLoggingCore(builder);
    }

    /// <summary>
    /// Registers HTTP client logging components into <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder" />.</param>
    /// <param name="section">The <see cref="IConfigurationSection"/> to use for configuring <see cref="LoggingOptions"/>.</param>
    /// <returns>
    /// An <see cref="IHttpClientBuilder" /> that can be used to configure the client.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any of the arguments is <see langword="null"/>.</exception>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(LoggingOptions))]
    [UnconditionalSuppressMessage("Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "Addressed with [DynamicDependency]")]
    public static IHttpClientBuilder AddEnrichableLogging(this IHttpClientBuilder builder, IConfigurationSection section)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(section);

        _ = builder.Services
            .AddValidatedOptions<LoggingOptions, LoggingOptionsValidator>(builder.Name)
            .Bind(section);

        AddEnrichableLoggingCore(builder);

        return AddEnrichableLoggingCore(builder);
    }

    /// <summary>
    /// Registers HTTP client logging components into <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder" />.</param>
    /// <param name="configure">The delegate to configure <see cref="LoggingOptions"/> with.</param>
    /// <returns>
    /// An <see cref="IHttpClientBuilder" /> that can be used to configure the client.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any of the arguments is <see langword="null"/>.</exception>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(LoggingOptions))]
    [UnconditionalSuppressMessage("Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "Addressed with [DynamicDependency]")]
    public static IHttpClientBuilder AddEnrichableLogging(this IHttpClientBuilder builder, Action<LoggingOptions> configure)
    {
        _ = Throw.IfNull(builder);
        _ = Throw.IfNull(configure);

        _ = builder.Services
            .AddValidatedOptions<LoggingOptions, LoggingOptionsValidator>(builder.Name)
            .Configure(configure);

        return AddEnrichableLoggingCore(builder);
    }

    /// <summary>
    /// Adds an enricher instance of <typeparamref name="T"/> to the <see cref="IServiceCollection"/> to enrich HTTP client logs.
    /// </summary>
    /// <typeparam name="T">Type of enricher.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the instance of <typeparamref name="T"/> to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddHttpClientLogEnricher<T>(this IServiceCollection services)
        where T : class, IHttpClientLogEnricher
    {
        _ = Throw.IfNull(services);

        _ = services.AddActivatedSingleton<IHttpClientLogEnricher, T>();

        return services;
    }
}
