﻿namespace Finance.Expensia.Shared.Constants
{
    public static class PermissionConstants
    {
        public const string TypeCode = "Permission";
        public const string SuperPermission = "Administrator";
        public const string RefreshToken = "RefreshToken";
        public const string MyPermission = "MyPermission";

        public static class OutgoingPayment
        {
            public const string OutgoingPaymentView = "OutgoingPayment.View";
            public const string OutgoingPaymentUpsert = "OutgoingPayment.Upsert";
            public const string OutgoingPaymentDelete = "OutgoingPayment.Delete";
        }

        public static class ApprovalInbox
        {
            public const string ApprovalInboxView = "ApprovalInbox.View";
            public const string ApprovalInboxUpdate = "ApprovalInbox.Update";
            public const string ApprovalInboxDelete = "ApprovalInbox.Delete";
        }

        public static class MasterData
        {
            public const string CompanyView = "MasterData.Company.View";
            public const string BankAliasView = "MasterData.BankAlias.View";
        }
    }
}
