using Finlay.PharmaVigilance.Application.DTO;
using Finlay.PharmaVigilance.Application.IServices;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace Finlay.PharmaVigilance.Infrastructure;

public class FriendlyCaptchaService : ICaptchaService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public FriendlyCaptchaService(
        IConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> VerifyToken(string token)
    {
        try
        {
            var apiKey = _config["FriendlyCaptcha:ApiKey"];
            var siteKey = _config["FriendlyCaptcha:SiteKey"];

            var client = _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var payload = new
            {
                response = token,
                sitekey = siteKey
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await client.PostAsync(
                "https://global.frcapi.com/api/v2/captcha/siteverify",
                content
            );

            var json = await response.Content.ReadAsStringAsync();


            var result = JsonSerializer.Deserialize<FriendlyCaptchaResponse>(json);

            if (result?.success != true)
            {
                // The body only carries an error code (no secrets, no personal data).
                Console.WriteLine($"Friendly Captcha rejected the token: {json}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }
}