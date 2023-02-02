﻿using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Components;

namespace Microsoft.Extensions.DependencyInjection;

public class CloudLoginService
{
    IServiceCollection AddCloudLogin { get; }
    //public CloudLoginConfiguration Options { get; set; }
}

public static class MvcServiceCollectionExtensions
{
    public static async Task<CloudLoginService> AddCloudLogin(this IServiceCollection services, HttpClient? httpServer = null)
    {
        CloudLoginServerClient cloudLoginClient = new()
        {
            HttpServer = httpServer
        };

        if (httpServer != null)
        {
            try
            {
                cloudLoginClient = await cloudLoginClient.InitFromServer();
            }
            catch (Exception e)
            {
                throw e;
            }
            cloudLoginClient.HttpServer = httpServer;
        }

        services.AddSingleton(cloudLoginClient);

        return null;
    }
}