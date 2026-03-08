using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entites
{
    public class Tenant
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Subdomain { get; set; } = string.Empty;
        public TenantStatus Status { get; set; } = TenantStatus.Active;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public List<User> Users { get; set; } = [];
        public Subscription? Subscription { get; set; }
    }

    public enum TenantStatus
    {
        Active,
        Suspended,
        Deleted
    }
}
