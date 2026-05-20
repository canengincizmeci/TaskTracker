using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using TaskTracker.Core.Extensions;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Security.Encryption;

namespace TaskTracker.Core.Utilities.Security.Jwt
{
    public class JwtHelper : ITokenHelper
    {
        private readonly IConfiguration _configuration;

        private TokenOptions _tokenOptions;
        private DateTime _accessTokenExpiration;
        public JwtHelper(IConfiguration configuration)
        {
            _configuration = configuration;
            _tokenOptions = _configuration.GetSection("TokenOptions").Get<TokenOptions>();
            _accessTokenExpiration = DateTime.UtcNow.AddMinutes(_tokenOptions.AccessTokenExpiration);
        }

        public AccessToken CreateToken(User user, List<OperationClaim> operationClaims)
        {
            var accessTokenExpiration =
                DateTime.UtcNow.AddMinutes(_tokenOptions.AccessTokenExpiration);

            var securityKey = SecurityKeyHelper.CreateSecurityKey(_tokenOptions.SecurityKey);
            var signingCredentials = SigningCredentialsHelper.CreateSigningCredentials(securityKey);

            var jwt = CreateJwtSecurityToken(
                _tokenOptions,
                user,
                signingCredentials,
                operationClaims,
                accessTokenExpiration
            );

            var token = new JwtSecurityTokenHandler().WriteToken(jwt);

            return new AccessToken
            {
                Token = token,
                Expiration = accessTokenExpiration
            };
        }

        public JwtSecurityToken CreateJwtSecurityToken(TokenOptions tokenOptions, User user, SigningCredentials signingCredentials, List<OperationClaim> operationClaims, DateTime accessTokenExpiration)
        {
            var jwt = new JwtSecurityToken(
                issuer: tokenOptions.Issuer,
                audience: tokenOptions.Audience,
                //expires: _accessTokenExpiration,
                //notBefore: DateTime.Now,
                expires: accessTokenExpiration,
                notBefore: DateTime.UtcNow,
                claims: SetClaims(user, operationClaims),
                signingCredentials: signingCredentials
            );
            return jwt;
        }

        private IEnumerable<Claim> SetClaims(User user, List<OperationClaim> operationClaims)
        {
            var claims = new List<Claim>();
            claims.AddNameIdentifier(user.Id.ToString());
            claims.AddEmail(user.Email);
            claims.AddName($"{user.FirstName} {user.LastName}");
            claims.AddRoles(operationClaims.Select(c => c.Name).ToArray());

            return claims;
        }

        public RefreshToken CreateRefreshToken(int userId)
        {
            return new RefreshToken
            {
                UserId = userId,
                Token = Guid.NewGuid().ToString(),
                Expires = DateTime.UtcNow.AddDays(_tokenOptions.RefreshTokenExpirationDays),
                CreatedAt = DateTime.UtcNow,
                IsRevoked = false
            };


        }
    }
}
