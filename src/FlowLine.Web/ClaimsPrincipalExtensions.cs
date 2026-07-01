using System.Security.Claims;
using FlowLine.Application.Staff;

namespace FlowLine.Web;

/// <summary>Reads FlowLine's auth-cookie claims off the signed-in principal.</summary>
public static class ClaimsPrincipalExtensions
{
    public static int GetLevel(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(FlowLineClaims.Level)?.Value, out var level) ? level : AccessLevel.Staff;

    public static int GetStaffNumber(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var number) ? number : 0;
}
