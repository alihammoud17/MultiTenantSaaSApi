using Domain.Entites;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Interfaces
{
    public interface IJwtService
    {
        string GenerateToken(User user, Tenant tenant);
    }
}
