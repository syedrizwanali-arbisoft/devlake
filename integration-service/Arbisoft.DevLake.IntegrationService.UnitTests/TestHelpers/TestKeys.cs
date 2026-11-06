// Licensed to the Apache Software Foundation (ASF) under one or more
// contributor license agreements.  See the NOTICE file distributed with
// this work for additional information regarding copyright ownership.
// The ASF licenses this file to You under the Apache License, Version 2.0
// (the "License"); you may not use this file except in compliance with
// the License.  You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Security.Cryptography;
using Arbisoft.DevLake.IntegrationService.DataAccess.Models;
using Microsoft.IdentityModel.Tokens;

namespace Arbisoft.DevLake.IntegrationService.UnitTests.TestHelpers;

public sealed class TestKey
{
    private TestKey(string kid, string privatePem, SigningKey row)
    {
        Kid = kid;
        PrivatePem = privatePem;
        Row = row;
    }

    public string Kid { get; }
    public string PrivatePem { get; }
    public SigningKey Row { get; }

    public static TestKey Create(long id, string kid, DateTime activatedOn)
    {
        using var rsa = RSA.Create(2048);
        return new TestKey(kid, rsa.ExportRSAPrivateKeyPem(), new SigningKey
        {
            Id = id,
            Kid = kid,
            Algorithm = SecurityAlgorithms.RsaSha256,
            PublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            PrivateKeyRef = kid,
            ActivatedOn = activatedOn,
            CreatedOn = activatedOn,
        });
    }
}
