﻿using Finance.Expensia.DataAccess.Bases;
using Finance.Expensia.Shared.Enums;

namespace Finance.Expensia.DataAccess.Models
{
    public class ApprovalHistory : EntityBase
    {
        public Guid ApprovalRuleId { get; set; }
        public string TransactionNo { get; set; } = string.Empty;
        public ApprovalStatus ApprovalStatus { get; set; }
        public string ApprovalRoleCode { get; set; } = string.Empty;
        public string ApprovalRoleDesc { get; set; } = string.Empty;
        public Guid ApprovalUserId { get; set; }
        public string ApprovalName { get; set; } = string.Empty;
        public int ApprovalLevel { get; set; }
    }
}
