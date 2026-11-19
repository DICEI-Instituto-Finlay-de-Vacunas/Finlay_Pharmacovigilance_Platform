using System.ComponentModel.DataAnnotations;

namespace Finlay.PharmaVigilance.Application.DTO;


public class PublicAefiReportDto : ReportDto
{
    /// <summary>
    /// Friendly Captcha solution. Required for anonymous reporters; signed-in medical reviewers and
    /// administrators submit without it (the form hides the widget for them).
    /// </summary>
    public string? Token { get; set; }

    [Required(ErrorMessage = "Reporter information is required.")]
    public ReporterDto Reporter { get; set; } = null!;

}