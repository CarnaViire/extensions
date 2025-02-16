﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace System.Cloud.Messaging.DependencyInjection.Tests.Data.Consumers;

internal class DerivedConsumer : MessageConsumer
{
    public DerivedConsumer(IMessageSource source, IReadOnlyList<IMessageMiddleware> middlewares, MessageDelegate messageDelegate, ILogger logger)
        : base(source, middlewares, messageDelegate, logger)
    {
    }

    protected override ValueTask HandleMessageProcessingFailureAsync(MessageContext context, Exception exception) => default;

    protected override ValueTask ProcessingStepAsync(CancellationToken cancellationToken) => FetchAndProcessMessageAsync(cancellationToken);

    protected override bool ShouldStopConsumer(MessageContext context)
    {
        var payload = context.GetUTF8SourcePayloadAsString();
        return string.IsNullOrEmpty(payload);
    }
}
