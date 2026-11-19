using Finlay.PharmaVigilance.Application.DTO;
using Finlay.PharmaVigilance.Application.IServices;
using Finlay.PharmaVigilance.Domain.Enum;
using Finlay.PharmaVigilance.Domain.Events;
using MassTransit;
using Microsoft.Extensions.Configuration;


namespace Finlay.PharmaVigilance.Infrastructure.Consumers;

public class RegisterUserConsumer : IConsumer<RegisterUserEvent>
{
    private readonly IEmailService _emailService;
    private readonly string _frontendBaseUrl;

    public RegisterUserConsumer(IEmailService emailService, IConfiguration configuration)
    {
        _emailService = emailService;
        // Public URL of the web frontend, used in the activation link (Frontend__BaseUrl).
        _frontendBaseUrl = (configuration["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
    }

    public async Task Consume(ConsumeContext<RegisterUserEvent> context)
    {
        var data = context.Message;

        var url =
            $"{_frontendBaseUrl}/activate-account" +
            $"?email={data.Email}" +
            $"&token={Uri.EscapeDataString(data.Token)}";


        Console.WriteLine($"Enviando email de activación a {data.Email} con URL: {url}");

        await _emailService.SendEmailAsync(
            data.Email!,
            EmailTemplateType.ActivateAccount,
            new ActivateAccountTemplate
            {
                FullName = data.FullName,
                ActivationLink = url
            }
        );

    }

}