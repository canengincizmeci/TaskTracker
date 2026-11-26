using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Utilities.Security.Jwt
{
    public interface ITokenHelper
    {
        AccessToken CreateToken(User user, List<OperationClaim> operationClaims, Guid sessionId);
        (string Credential, RefreshToken Record) CreateRefreshToken(int userId, Guid sessionId);

    }
}
