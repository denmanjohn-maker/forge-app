using Microsoft.AspNetCore.Identity;

namespace MtgForge.Api.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}
