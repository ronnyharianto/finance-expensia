﻿using Finance.Expensia.Shared.Enums;
using System.Text.Json.Serialization;

namespace Finance.Expensia.Core.Services.OutgoingPayment.Dtos
{
	public class ListOutgoingPaymentDto
	{
		public Guid OutgoingPaymentId { get; set; }
		public string TransactionNo { get; set; } = string.Empty;
		public string CompanyName { get; set; } = string.Empty;
		public DateTime RequestDate { get; set; }
		public DateTime? ScheduledDate { get; set; }
		public string Requestor { get; set; } = string.Empty;
		public decimal TotalAmount { get; set; }
		public string Remark { get; set; } = string.Empty;
		public string FromBankAlias { get; set; } = string.Empty;
		public string ToBankAlias { get; set; } = string.Empty;
		public ApprovalStatus ApprovalStatus { get; set; }
		[JsonIgnore]
		public string CreatedBy { get; set; } = string.Empty;
        public bool IsUserAllowEdit { get; set; }
        public List<OutgoingPaymentTaggingDto> OutgoingPaymentTaggings { get; set; } = [];
	}
}
