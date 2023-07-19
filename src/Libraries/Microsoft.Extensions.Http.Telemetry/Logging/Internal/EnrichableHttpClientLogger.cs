// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Telemetry.Logging;
using Microsoft.Shared.Diagnostics;
using Microsoft.Shared.Pools;
using Microsoft.Extensions.Http.Logging;

namespace Microsoft.Extensions.Http.Telemetry.Logging.Internal;

internal class EnrichableHttpClientLogger : IHttpClientAsyncLogger
{
    internal TimeProvider TimeProvider = TimeProvider.System;

    private readonly IHttpClientLogEnricher[] _enrichers;
    private readonly IHttpRequestReader _httpRequestReader;

    private readonly ObjectPool<List<KeyValuePair<string, string>>> _headersPool =
        PoolFactory.CreateListPool<KeyValuePair<string, string>>();

    private readonly ObjectPool<LogRecord> _logRecordPool =
        PoolFactory.CreatePool(new LogRecordPooledObjectPolicy());

    private readonly bool _logRequestStart;
    private readonly bool _logRequestHeaders;
    private readonly bool _logResponseHeaders;

    public ILogger Logger { get; private set; }

    public EnrichableHttpClientLogger(
        ILogger<EnrichableHttpClientLogger> logger,
        IHttpRequestReader httpRequestReader,
        IEnumerable<IHttpClientLogEnricher> enrichers,
        IOptions<LoggingOptions> options)
    {
        _httpRequestReader = httpRequestReader;
        _enrichers = enrichers.ToArray();
        _ = Throw.IfMemberNull(options, options.Value);

        Logger = logger;

        _logRequestStart = options.Value.LogRequestStart;
        _logResponseHeaders = options.Value.ResponseHeadersDataClasses.Count > 0;
        _logRequestHeaders = options.Value.RequestHeadersDataClasses.Count > 0;
    }

    public async ValueTask<object?> LogRequestStartAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var timestamp = TimeProvider.GetTimestamp();

        var logRecord = _logRecordPool.Get();

        List<KeyValuePair<string, string>>? requestHeadersBuffer = null;

        if (_logRequestHeaders)
        {
            requestHeadersBuffer = _headersPool.Get();
        }

        await _httpRequestReader.ReadRequestAsync(logRecord, request, requestHeadersBuffer, cancellationToken).ConfigureAwait(false);

        if (_logRequestStart)
        {
            Log.OutgoingRequest(Logger, LogLevel.Information, logRecord);
        }

        return logRecord;
    }

    public async ValueTask LogRequestStopAsync(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage response,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        var logRecord = (LogRecord?)context;
        _ = Throw.IfNull(logRecord);

        List<KeyValuePair<string, string>>? responseHeadersBuffer = null;
        if (_logResponseHeaders)
        {
            responseHeadersBuffer = _headersPool.Get();
        }

        await _httpRequestReader.ReadResponseAsync(logRecord, response, responseHeadersBuffer, cancellationToken).ConfigureAwait(false);

        var propertyBag = LogMethodHelper.GetHelper();
        FillLogRecord(logRecord, propertyBag, elapsed, request, response);
        Log.OutgoingRequest(Logger, GetLogLevel(logRecord), logRecord);

        ReturnToPool(logRecord, propertyBag);
    }

    public ValueTask LogRequestFailedAsync(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage? response,
        Exception exception,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        LogRequestFailed(context, request, response, exception, elapsed);
        return default; // completed task
    }

    public void LogRequestFailed(object? context, HttpRequestMessage request, HttpResponseMessage? response, Exception exception, TimeSpan elapsed)
    {
        var logRecord = (LogRecord?)context;
        _ = Throw.IfNull(logRecord);

        var propertyBag = LogMethodHelper.GetHelper();
        FillLogRecord(logRecord, propertyBag, elapsed, request, response);
        Log.OutgoingRequestError(Logger, logRecord, exception);

        ReturnToPool(logRecord, propertyBag);
    }

    public object? LogRequestStart(HttpRequestMessage request)
    {
        throw new NotImplementedException(); // should be implemented
    }

    public void LogRequestStop(object? context, HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed)
    {
        throw new NotImplementedException(); // should be implemented
    }

    private void ReturnToPool(LogRecord logRecord, LogMethodHelper propertyBag)
    {
        LogMethodHelper.ReturnHelper(propertyBag);

        if (logRecord.RequestHeaders is not null)
        {
            _headersPool.Return(logRecord.RequestHeaders);
        }
        if (logRecord.ResponseHeaders is not null)
        {
            _headersPool.Return(logRecord.ResponseHeaders);
        }
        _logRecordPool.Return(logRecord);
    }

    private void FillLogRecord(
        LogRecord logRecord, LogMethodHelper propertyBag, TimeSpan elapsed,
        HttpRequestMessage request, HttpResponseMessage? response)
    {
        foreach (var enricher in _enrichers)
        {
            try
            {
                enricher.Enrich(propertyBag, request, response);
            }
            catch (Exception e)
            {
                Log.EnrichmentError(Logger, e);
            }
        }

        logRecord.EnrichmentProperties = propertyBag;
        logRecord.Duration = (long)elapsed.TotalMilliseconds;
    }

    private static LogLevel GetLogLevel(LogRecord logRecord)
    {
        const int HttpErrorsRangeStart = 400;
        const int HttpErrorsRangeEnd = 599;
        int statusCode = logRecord.StatusCode!.Value;

        if (statusCode >= HttpErrorsRangeStart && statusCode <= HttpErrorsRangeEnd)
        {
            return LogLevel.Error;
        }

        return LogLevel.Information;
    }
}
