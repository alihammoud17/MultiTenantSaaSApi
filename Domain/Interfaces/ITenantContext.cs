using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Interfaces
{
    public interface ITenantContext
    {
        Guid TenantId { get; }
        void SetTenantId(Guid tenantId);
    }
}
