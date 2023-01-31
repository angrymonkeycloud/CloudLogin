using System.Net.Http.Json;
using System.Web;
using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Login.DataContract;
using AngryMonkey.CloudLogin.Models;

namespace AngryMonkey.CloudLogin;

public class CloudLoginClient : CloudLoginClientBase
{
	public HttpClient? HttpServer { get; set; }

	public CloudLoginClient(string baseAddress)
	{
		HttpServer = new() { BaseAddress = new(baseAddress) };
	}

	public async Task<UserModel?> GetUserById(Guid userId)
	{
		try
		{
			HttpResponseMessage message = await HttpServer.GetAsync($"Api/Client/GetUserById?id={HttpUtility.UrlEncode(userId.ToString())}");

			if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

			CloudUser? selectedUser = await message.Content.ReadFromJsonAsync<CloudUser?>();

			if (selectedUser == null) return null;

			return Parse(selectedUser);
		}
		catch
		{
			throw;
		}

	}
	public async Task<List<UserModel>?> GetUsersByDisplayName(string displayName)
	{
		try
		{
			HttpResponseMessage message = await HttpServer.GetAsync($"Api/Client/GetUsersByDisplayName?displayname={HttpUtility.UrlEncode(displayName)}");

			if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

			List<CloudUser>? selectedUser = await message.Content.ReadFromJsonAsync<List<CloudUser>?>();

			if (selectedUser == null) return null;

			return Parse(selectedUser);
		}
		catch
		{
			throw;
		}

	}
	public async Task<UserModel> GetUserByRequestId(Guid requestId, int minutesToExpiry)
	{
		try
		{
			HttpResponseMessage message = await HttpServer.GetAsync($"Api/Client/GetUserByRequestId?requestId={HttpUtility.UrlEncode(requestId.ToString())}&minutesToExpiry={minutesToExpiry}");

			if (message.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

			CloudUser? selectedUser = await message.Content.ReadFromJsonAsync<CloudUser>();

			if (selectedUser == null) return null;

			return Parse(selectedUser);
		}
		catch
		{
			throw;
		}
	}
}