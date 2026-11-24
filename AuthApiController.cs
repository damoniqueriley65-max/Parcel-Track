using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using ParcelTrack.Models;

namespace ParcelTrack.Controllers.Api
{
    // POST /api/auth/login: turns an email and password into a JWT.
    // The browser pages keep using the Identity cookie; this endpoint is for programs
    // (the branch desk program) that have no cookie and need a 401, not a 302 to /login.
    [ApiController]
    [Route("api/auth")]
    public class AuthApiController : ControllerBase
    {
        private readonly SignInManager<IdentityUser> _signIn;
        private readonly UserManager<IdentityUser> _users;
        private readonly IConfiguration _config;

        public AuthApiController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users, IConfiguration config)
        {
            _signIn = signIn;
            _users = users;
            _config = config;
        }

        // POST /api/auth/login with { "email": ..., "password": ... }.
        // Right details: 200 + a token that lasts 60 minutes and carries the user's roles.
        // A wrong password or an unknown email: 401, with the SAME message for both, so a
        // caller cannot tell which was wrong. An email that isn't an email at all: 400.
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginInput input)
        {
            IdentityUser? user = await _users.FindByEmailAsync(input.Email);
            // Same message whether the email is unknown or the password is wrong.
            const string badMessage = "The email or password is wrong";

            if (user == null)
            {
                return Unauthorized(new { message = badMessage });
            }

            Microsoft.AspNetCore.Identity.SignInResult result =
                await _signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                return Unauthorized(new { message = badMessage });
            }

            // The token carries the user's roles, so [Authorize(Policy = "CanManageParcels")]
            // and [Authorize(Roles = "Admin")] on the API can see them.
            IList<string> roles = await _users.GetRolesAsync(user);
            List<Claim> claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? input.Email),
                new Claim(ClaimTypes.Name, user.UserName ?? input.Email)
            };
            foreach (string role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            IConfigurationSection jwt = _config.GetSection("Jwt");
            int lifetimeMinutes = jwt.GetValue<int?>("TokenLifetimeMinutes") ?? 60;
            DateTime expiresAt = DateTime.UtcNow.AddMinutes(lifetimeMinutes);

            SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
            SigningCredentials creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            JwtSecurityToken token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                expires: expiresAt,
                signingCredentials: creds);

            return Ok(new LoginResult
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                ExpiresAt = expiresAt
            });
        }
    }
}
