﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.SignalR.SignalRService
{
    public class ServiceManager
    {
        public ServiceHubContext CreateHubContextAsync(string hubName)
        {
            throw new NotImplementedException();
        }

        public string GenerateAccessToken(string audience, TimeSpan? lifeTime = null)
        {
            throw new NotImplementedException();
        }
    }
}