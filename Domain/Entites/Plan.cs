using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entites
{
    public class Plan
    {
        public string Id { get; set; } = string.Empty; // "plan-free", "plan-pro"
        public string Name { get; set; } = string.Empty;
        public decimal MonthlyPrice { get; set; }
        public int ApiCallsPerMonth { get; set; }
        public int MaxUsers { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; }
    }
}
