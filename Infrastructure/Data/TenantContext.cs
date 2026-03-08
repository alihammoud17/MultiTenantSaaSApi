using Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Data
{
    public class TenantContext : ITenantContext
    {
        private Guid _tenantId;

        public Guid TenantId => _tenantId;

        public void SetTenantId(Guid tenantId)
        {
            if (_tenantId != Guid.Empty && _tenantId != tenantId)
                throw new InvalidOperationException("Tenant ID already set for this request");

            _tenantId = tenantId;
        }
    }
}
