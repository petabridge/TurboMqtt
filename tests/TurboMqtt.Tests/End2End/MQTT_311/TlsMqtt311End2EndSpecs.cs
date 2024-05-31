// -----------------------------------------------------------------------
// <copyright file="TlsMqtt311End2EndSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Akka.Event;
using TurboMqtt.Client;
using TurboMqtt.IO.Tcp;
using Xunit.Abstractions;

namespace TurboMqtt.Tests.End2End;

[Collection(nameof(TcpEnd2EndCollection))]
public class TlsMqtt311End2EndSpecs : TcpMqtt311End2EndSpecs
{
    // This is a workaround for this issue:
    // https://github.com/dotnet/runtime/issues/23749
    private static readonly X509Certificate2 RootCert = new (
        X509Certificate2.CreateFromEncryptedPemFile("./certs/root_cert.pem", "password")
            .Export(X509ContentType.Pkcs12));

    private static readonly X509ChainPolicy RootChainPolicy = new()
    {
        CustomTrustStore = { RootCert },
        TrustMode = X509ChainTrustMode.CustomRootTrust, 
        RevocationMode = X509RevocationMode.NoCheck
    };

    private static readonly X509Chain RootChain = new ()
    {
        ChainPolicy = RootChainPolicy
    };

    public TlsMqtt311End2EndSpecs(ITestOutputHelper output) : base(output)
    {
    }
    
    // This is a workaround for this issue:
    // https://github.com/dotnet/runtime/issues/23749
    private static readonly X509Certificate2 ServerCert =  new X509Certificate2(
        X509Certificate2.CreateFromEncryptedPemFile("./certs/server_cert.pem", "password")
            .Export(X509ContentType.Pkcs12));
    
    protected override MqttTcpServerOptions DefaultTcpServerOptions => new ("localhost", 21883)
    {
        SslOptions = new SslServerAuthenticationOptions
        {
            ServerCertificate = ServerCert,
            ClientCertificateRequired = false,
            RemoteCertificateValidationCallback = ValidateClientCertificate
        } 
    };
    
    private bool ValidateClientCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        // Return true if client certificate is not required
        if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNotAvailable)
            return true;
        
        // Validate client certificate with a custom chain
        if (certificate is not null)
        {
            var isValid = RootChain.Build(new X509Certificate2(certificate));
            if (!isValid)
            {
                foreach (var status in RootChain.ChainStatus)
                {
                    Log.Error("[Server] Chain error: {0}", status.StatusInformation);
                }
            }

            return isValid;
        }

        // Refuse everything else
        Log.Error("[Server] Certificate error: {0}", sslPolicyErrors);
        return false;
    }    

    private bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        // Missing cert or the destination hostname wasn't valid for the cert.
        if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            return false;

        // Validate client certificate with a custom chain
        if (certificate is not null)
        {
            chain ??= RootChain;
            var isValid = chain.Build(new X509Certificate2(certificate));
            if (!isValid)
            {
                foreach (var status in chain.ChainStatus)
                {
                    Log.Error("[Client] Chain error: [{0}] {1}", status.Status, status.StatusInformation);
                }
            }

            return isValid;
        }
        
        // Refuse everything else
        Log.Error("[Client] Certificate error: {0}", errors);
        return false;
    }
    
    protected override MqttClientTcpOptions DefaultTcpOptions => new("localhost", 21883)
    {
        TlsOptions = new ClientTlsOptions
        {
            UseTls = true,
            SslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                CertificateChainPolicy = RootChainPolicy,
                RemoteCertificateValidationCallback = ValidateServerCertificate
            }
        }
    };
}