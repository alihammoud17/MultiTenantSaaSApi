using Domain.Entities;
using Domain.Outputs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Interfaces
{
    public interface IJwtService
    {
        AccessToken GenerateToken(User user, Tenant tenant);
    }
}
