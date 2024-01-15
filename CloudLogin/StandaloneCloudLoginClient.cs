﻿using AngryMonkey.Cloud;
using Newtonsoft.Json;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Web;


namespace AngryMonkey.CloudLogin;

public class CloudLoginStandaloneClient
{
    public required HttpClient HttpServer { get; init; }
    public string LoginUrl => HttpServer.BaseAddress.AbsoluteUri;

    public string UserRoute = "Account";

    //Configuration
    public static CloudLoginStandaloneClient Build(string baseUrl)
    {
        HttpClient httpClient = new() { BaseAddress = new(baseUrl) };

        return new()
        {
            HttpServer = httpClient,
        };
    }

    //Get user(s) information from cookie
    public async Task<bool> AutomaticLogin()
    {
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{@UserRoute}/AutomaticLogin");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                return false;

            return await message.Content.ReadFromJsonAsync<bool>();
        }
        catch { throw; }
    }
    public async Task<User?> CurrentUser()
    {
        //if (accessor == null)
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{@UserRoute}/CurrentUser");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;

            return await message.Content.ReadFromJsonAsync<User>();
        }
        catch
        {
            return null;
        }
    }
    public async Task<bool> IsAuthenticated()
    {
        //if (accessor == null)
        try
        {
            HttpResponseMessage message = await HttpServer.GetAsync($"{@UserRoute}/IsAuthenticated");

            if (message.StatusCode == System.Net.HttpStatusCode.NoContent)
                return false;

            return await message.Content.ReadFromJsonAsync<bool>();
        }
        catch { throw; }
    }
}