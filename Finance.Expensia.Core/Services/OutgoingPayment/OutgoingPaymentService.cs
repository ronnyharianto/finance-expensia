﻿using AutoMapper;
using Finance.Expensia.Core.Services.Inbox;
using Finance.Expensia.Core.Services.OutgoingPayment.Dtos;
using Finance.Expensia.Core.Services.OutgoingPayment.Inputs;
using Finance.Expensia.DataAccess;
using Finance.Expensia.DataAccess.Models;
using Finance.Expensia.Shared.Enums;
using Finance.Expensia.Shared.Objects;
using Finance.Expensia.Shared.Objects.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace Finance.Expensia.Core.Services.OutgoingPayment
{
    public class OutgoingPaymentService(ApplicationDbContext dbContext, IMapper mapper, ILogger<OutgoingPaymentService> logger) 
        : BaseService<OutgoingPaymentService>(dbContext, mapper, logger)
    {
        #region Query
        public async Task<ResponsePaging<ListOutgoingPaymentDto>> GetListOfOutgoingPayment(ListOutgoingPaymentFilterInput input)
        {
            var retVal = new ResponsePaging<ListOutgoingPaymentDto>();

            var dataOutgoingPayments = _dbContext.OutgoingPayments
				.Where(d => !input.CompanyId.HasValue || input.CompanyId.Equals(d.CompanyId))
				.Where(d => !input.ApprovalStatus.HasValue || input.ApprovalStatus.Equals(d.ApprovalStatus))
				.Where(d => !input.StartDate.HasValue || d.RequestDate >= input.StartDate)
                .Where(d => !input.EndDate.HasValue || d.RequestDate <= input.EndDate)
				.Where(d => 
					EF.Functions.Like(d.TransactionNo, $"%{input.SearchKey}%")
					|| EF.Functions.Like(d.Requestor, $"%{input.SearchKey}%")
					|| EF.Functions.Like(d.Remark, $"%{input.SearchKey}%"))
                .OrderByDescending(d => d.Modified ?? d.Created)
                .Select(d => _mapper.Map<ListOutgoingPaymentDto>(d));

            retVal.ApplyPagination(input.Page, input.PageSize, dataOutgoingPayments);

            return await Task.FromResult(retVal);
        }

		public async Task<ResponseObject<OutgoingPaymentDto>> GetDetailOutgoingPayment(Guid outgoingPaymentId)
		{
			var result = new ResponseObject<OutgoingPaymentDto>();
			var dataOutgoingPay = await _dbContext.OutgoingPayments
												  .Include(x => x.OutgoingPaymentDetails.OrderBy(d => d.Created))
												  .ThenInclude(x => x.OutgoingPaymentDetailAttachments)
												  .FirstOrDefaultAsync(x => x.Id == outgoingPaymentId);

			if (dataOutgoingPay != null)
			{
				var dataOutgoingPayDto = _mapper.Map<OutgoingPaymentDto>(dataOutgoingPay);
                return await Task.FromResult(new ResponseObject<OutgoingPaymentDto>(responseCode: ResponseCode.Ok)
                {
                    Obj = dataOutgoingPayDto,
                });

            }

            return await Task.FromResult(new ResponseObject<OutgoingPaymentDto>("Data outgoing payment tidak ditemukan", ResponseCode.NotFound));
        }
        #endregion

        #region Mutation
        public async Task<ResponseBase> CreateOutgoingPayment(CreateOutgoingPaymentInput input, CurrentUserAccessor currentUserAccessor)
        {
			if (input == null)
				return new ResponseBase("Tolong lengkapi informasi yang mandatory", ResponseCode.NotFound);

			if (input.ExpectedTransfer == ExpectedTransfer.Scheduled && !input.ScheduledDate.HasValue)
				return new ResponseBase("Schedule date harus diisi");

			if (input.OutgoingPaymentDetails.Count == 0 && input.IsSubmit)
				return new ResponseBase("Belum ada data detail", ResponseCode.NotFound);

			var dataCompany = await _dbContext.Companies.FirstOrDefaultAsync(d => d.Id.Equals(input.CompanyId));
            var dataFromBankAlias = await _dbContext.BankAliases.FirstOrDefaultAsync(d => d.Id.Equals(input.FromBankAliasId) && d.CompanyId.Equals(input.CompanyId));
			var dataToBankAlias = await _dbContext.BankAliases.FirstOrDefaultAsync(d => d.Id.Equals(input.ToBankAliasId));
            var dataTransactionType = await _dbContext.TransactionTypes.FirstOrDefaultAsync(d => d.Id.Equals(input.TransactionTypeId));

            if (dataCompany == null)
                return new ResponseBase("Data company tidak valid", ResponseCode.NotFound);

			if (dataFromBankAlias == null)
				return new ResponseBase("Data from bank alias tidak valid", ResponseCode.NotFound);

			if (dataToBankAlias == null)
				return new ResponseBase("Data to bank alias tidak valid", ResponseCode.NotFound);

            if (dataTransactionType == null)
                return new ResponseBase("Data to transaction type tidak valid", ResponseCode.NotFound);

            var dateNow = DateTime.Now;

			var runningNumberConfig = await GetRunningNumberDocument(dataTransactionType.TransactionTypeCode, dataCompany.CompanyCode, dateNow);

			#region set data entity => OutgoingPayment
			var dataOutgoingPayment = _mapper.Map<DataAccess.Models.OutgoingPayment>(input);

			dataOutgoingPayment.TransactionNo = $"{dataCompany.CompanyCode}-{dataTransactionType.TransactionTypeCode}-{dateNow:ddMMyy}-{runningNumberConfig.RunningNumber + 1}";
			dataOutgoingPayment.Requestor = currentUserAccessor.FullName;
			dataOutgoingPayment.RequestDate = dateNow.Date;
			dataOutgoingPayment.CompanyName = dataCompany.CompanyName;
			dataOutgoingPayment.TransactionTypeCode = dataTransactionType.TransactionTypeCode;
			dataOutgoingPayment.ApprovalStatus = input.IsSubmit ? ApprovalStatus.WaitingApproval : ApprovalStatus.Draft;

			dataOutgoingPayment.FromBankAliasName = dataFromBankAlias.AliasName;
			dataOutgoingPayment.FromBankName = dataFromBankAlias.BankName;
			dataOutgoingPayment.FromAccountNo = dataFromBankAlias.AccountNo;
			dataOutgoingPayment.FromAccountName = dataFromBankAlias.AccountName;

			dataOutgoingPayment.ToBankAliasName = dataToBankAlias.AliasName;
			dataOutgoingPayment.ToBankName = dataToBankAlias.BankName;
			dataOutgoingPayment.ToAccountNo = dataToBankAlias.AccountNo;
			dataOutgoingPayment.ToAccountName = dataToBankAlias.AccountName;


			dataOutgoingPayment.TotalAmount = input.OutgoingPaymentDetails.Sum(d => d.Amount);
			#endregion

			#region set data entity => OutgoingPaymentDetail & OutgoingPaymentDetailAttachment
			foreach (var outgoingPaymentDetailInput in input.OutgoingPaymentDetails)
			{
				var dataPartner = await _dbContext.Partners.FirstOrDefaultAsync(d => d.Id.Equals(outgoingPaymentDetailInput.PartnerId));
				var dataCoa = await _dbContext.ChartOfAccounts.FirstOrDefaultAsync(d => d.Id.Equals(outgoingPaymentDetailInput.ChartOfAccountId) && d.CompanyId.Equals(input.CompanyId));
				var dataCostCenter = await _dbContext.CostCenters.FirstOrDefaultAsync(d => d.Id.Equals(outgoingPaymentDetailInput.CostCenterId) && d.CompanyId.Equals(input.CompanyId));
				var dataPostingAccount = await _dbContext.Companies.FirstOrDefaultAsync(d => d.Id.Equals(outgoingPaymentDetailInput.PostingAccountId));

				if (dataPartner == null)
					return new ResponseBase("Data partner tidak valid", ResponseCode.NotFound);

				if (dataCoa == null)
					return new ResponseBase("Data chart of account tidak valid", ResponseCode.NotFound);

				if (dataCostCenter == null)
					return new ResponseBase("Data cost center tidak valid", ResponseCode.NotFound);

				if (dataPostingAccount == null)
					return new ResponseBase("Data posting account tidak valid", ResponseCode.NotFound);

				var dataOutgoingPaymentDetail = _mapper.Map<OutgoingPaymentDetail>(outgoingPaymentDetailInput);

				dataOutgoingPaymentDetail.PartnerName = dataPartner.PartnerName;
				dataOutgoingPaymentDetail.ChartOfAccountNo = dataCoa.AccountCode;
				dataOutgoingPaymentDetail.CostCenterCode = dataCostCenter.CostCenterCode;
				dataOutgoingPaymentDetail.CostCenterName = dataCostCenter.CostCenterName;
				dataOutgoingPaymentDetail.PostingAccountName = dataPostingAccount.CompanyName;
				dataOutgoingPaymentDetail.OutgoingPaymentDetailAttachments.AddRange(outgoingPaymentDetailInput.OutgoingPaymentDetailAttachments.Select(d => _mapper.Map<OutgoingPaymentDetailAttachment>(d)));

				dataOutgoingPayment.OutgoingPaymentDetails.Add(dataOutgoingPaymentDetail);
			}
			#endregion

			await _dbContext.AddAsync(dataOutgoingPayment);

            if (input.IsSubmit)
            {
                var isSuccessWorkflow = await CreateApprovalWorkflow(dataOutgoingPayment, currentUserAccessor);

                if (!isSuccessWorkflow)
                    return new ResponseBase("Gagal membuat workflow approval", ResponseCode.Error);
            }

            await _dbContext.SaveChangesAsync();

            _ = await UpdateRunningNumber(runningNumberConfig.Id);

            return new ResponseBase($"Data outgoing payment berhasil {(input.IsSubmit ? "disubmit" : "disimpan sebagai draft")}", ResponseCode.Ok);
		}
		
		public async Task<ResponseBase> EditOutgoingPayment(EditOutgoingPaymentInput input, CurrentUserAccessor currentUserAccessor)
		{
            if (input == null)
                return new ResponseBase("Tolong lengkapi informasi yang mandatory", ResponseCode.NotFound);

			if (input.ExpectedTransfer == ExpectedTransfer.Scheduled && !input.ScheduledDate.HasValue)
				return new ResponseBase("Schedule date harus diisi");

			if (input.OutgoingPaymentDetails.Count == 0 && input.IsSubmit)
                return new ResponseBase("Belum ada data detail", ResponseCode.NotFound);

            var dataCompany = await _dbContext.Companies.FirstOrDefaultAsync(d => d.Id.Equals(input.CompanyId));
            var dataFromBankAlias = await _dbContext.BankAliases.FirstOrDefaultAsync(d => d.Id.Equals(input.FromBankAliasId) && d.CompanyId.Equals(input.CompanyId));
            var dataToBankAlias = await _dbContext.BankAliases.FirstOrDefaultAsync(d => d.Id.Equals(input.ToBankAliasId));
            var dataTransactionType = await _dbContext.TransactionTypes.FirstOrDefaultAsync(d => d.Id.Equals(input.TransactionTypeId));

            if (dataCompany == null)
                return new ResponseBase("Data company tidak valid", ResponseCode.NotFound);

            if (dataFromBankAlias == null)
                return new ResponseBase("Data from bank alias tidak valid", ResponseCode.NotFound);

            if (dataToBankAlias == null)
                return new ResponseBase("Data to bank alias tidak valid", ResponseCode.NotFound);
            if (dataTransactionType == null)
                return new ResponseBase("Data to transaction type tidak valid", ResponseCode.NotFound);

            var existOutgoing = await _dbContext.OutgoingPayments.Include(x => x.OutgoingPaymentDetails)
				.ThenInclude(x => x.OutgoingPaymentDetailAttachments)
				.FirstOrDefaultAsync(x => x.Id == input.Id);

			if (existOutgoing == null)
                return await Task.FromResult(new ResponseObject<OutgoingPaymentDto>("Data outgoing payment tidak ditemukan", ResponseCode.NotFound));

            #region edit data outgoing payment
            existOutgoing.CompanyName = dataCompany.CompanyName;
			existOutgoing.TransactionTypeId = dataTransactionType.Id;
			existOutgoing.TransactionTypeCode = dataTransactionType.TransactionTypeCode;
            existOutgoing.ApprovalStatus = input.IsSubmit ? ApprovalStatus.WaitingApproval : ApprovalStatus.Draft;

            existOutgoing.FromBankAliasName = dataFromBankAlias.AliasName;
            existOutgoing.FromBankName = dataFromBankAlias.BankName;
            existOutgoing.FromAccountNo = dataFromBankAlias.AccountNo;
            existOutgoing.FromAccountName = dataFromBankAlias.AccountName;

            existOutgoing.ToBankAliasName = dataToBankAlias.AliasName;
            existOutgoing.ToBankName = dataToBankAlias.BankName;
            existOutgoing.ToAccountNo = dataToBankAlias.AccountNo;
            existOutgoing.ToAccountName = dataToBankAlias.AccountName;
			existOutgoing.ExpectedTransfer = input.ExpectedTransfer;
			existOutgoing.BankPaymentType = input.BankPaymentType;
			existOutgoing.Remark = input.Remark;
			existOutgoing.ScheduledDate = input.ScheduledDate;

			existOutgoing.TotalAmount = input.OutgoingPaymentDetails.Sum(d => d.Amount);
			#endregion

			await _dbContext.SaveChangesAsync();

			#region edit data outgoing payment detail

			//delete outgoing detail
			var deleteOutgoingDetails = await _dbContext.OutgoingPaymentDetails
														.Include(x => x.OutgoingPaymentDetailAttachments)
														.Where(x => 
															x.OutgoingPaymentId == input.Id 
															&& !input.OutgoingPaymentDetails.Select(d => d.Id).Contains(x.Id))
														.ToListAsync();

			if (deleteOutgoingDetails.Count != 0)
			{
				foreach (var deleteOutgoingDetail in deleteOutgoingDetails)
				{
					deleteOutgoingDetail.RowStatus = 1;
					foreach (var attachment in deleteOutgoingDetail.OutgoingPaymentDetailAttachments)
					{
						attachment.RowStatus = 1;
					}
					_dbContext.Update(deleteOutgoingDetail);
				}
			}

			// add / edit outgoing payment detail
			foreach (var outgoingPaymentDetailInput in input.OutgoingPaymentDetails)
			{
                var dataPartner = await _dbContext.Partners.FirstOrDefaultAsync(d => d.Id.Equals(outgoingPaymentDetailInput.PartnerId));
                var dataCoa = await _dbContext.ChartOfAccounts.FirstOrDefaultAsync(d => d.Id.Equals(outgoingPaymentDetailInput.ChartOfAccountId) && d.CompanyId.Equals(input.CompanyId));
                var dataCostCenter = await _dbContext.CostCenters.FirstOrDefaultAsync(d => d.Id.Equals(outgoingPaymentDetailInput.CostCenterId) && d.CompanyId.Equals(input.CompanyId));
				var dataPostingAccount = await _dbContext.Companies.FirstOrDefaultAsync(d => d.Id.Equals(outgoingPaymentDetailInput.PostingAccountId));

				if (dataPartner == null)
                    return new ResponseBase("Data partner tidak valid", ResponseCode.NotFound);

                if (dataCoa == null)
                    return new ResponseBase("Data chart of account tidak valid", ResponseCode.NotFound);

                if (dataCostCenter == null)
                    return new ResponseBase("Data cost center tidak valid", ResponseCode.NotFound);

				if (dataPostingAccount == null)
					return new ResponseBase("Data posting account tidak valid", ResponseCode.NotFound);

				var existOutgoingDetail = existOutgoing.OutgoingPaymentDetails.FirstOrDefault(x => x.Id == outgoingPaymentDetailInput.Id);
				if (existOutgoingDetail != null)
				{
					//Edit data yang sudah ada
					existOutgoingDetail.InvoiceDate = outgoingPaymentDetailInput.InvoiceDate;
					existOutgoingDetail.Description = outgoingPaymentDetailInput.Description;
					existOutgoingDetail.PartnerId = outgoingPaymentDetailInput.PartnerId;
					existOutgoingDetail.PartnerName = dataPartner.PartnerName;
					existOutgoingDetail.ChartOfAccountId = outgoingPaymentDetailInput.ChartOfAccountId;
					existOutgoingDetail.ChartOfAccountNo = dataCoa.AccountCode;
					existOutgoingDetail.CostCenterId = outgoingPaymentDetailInput.CostCenterId;
					existOutgoingDetail.CostCenterCode = dataCostCenter.CostCenterCode;
                    existOutgoingDetail.CostCenterName = dataCostCenter.CostCenterName;
					existOutgoingDetail.PostingAccountId = outgoingPaymentDetailInput.PostingAccountId;
					existOutgoingDetail.PostingAccountName = dataPostingAccount.CompanyName;
					existOutgoingDetail.Amount = outgoingPaymentDetailInput.Amount;

					//delete attachment
					var deletedAttachments = existOutgoingDetail.OutgoingPaymentDetailAttachments
																.Where(x => 
																	!outgoingPaymentDetailInput.OutgoingPaymentDetailAttachments.Select(d => d.Id).Contains(x.Id));

					if (deletedAttachments.Any())
					{
						foreach (var deletedAttachment in deletedAttachments)
						{
							deletedAttachment.RowStatus = 1;
							_dbContext.Update(deletedAttachment);
						}
					}

					//add attachment
					foreach (var outgoingPaymentDetailAttachmentInput in outgoingPaymentDetailInput.OutgoingPaymentDetailAttachments.Where(d => !d.Id.HasValue))
					{
						var newOutgoingDetailAttachment = _mapper.Map<OutgoingPaymentDetailAttachment>(outgoingPaymentDetailAttachmentInput);
						newOutgoingDetailAttachment.OutgoingPaymentDetailId = existOutgoingDetail.Id;

						await _dbContext.AddAsync(newOutgoingDetailAttachment);
					}

					_dbContext.Update(existOutgoingDetail);
				}
				else
				{
					//Add data baru
                    var dataOutgoingPaymentDetail = _mapper.Map<OutgoingPaymentDetail>(outgoingPaymentDetailInput);

					dataOutgoingPaymentDetail.OutgoingPaymentId = existOutgoing.Id;
					dataOutgoingPaymentDetail.PartnerName = dataPartner.PartnerName;
                    dataOutgoingPaymentDetail.ChartOfAccountNo = dataCoa.AccountCode;
                    dataOutgoingPaymentDetail.CostCenterCode = dataCostCenter.CostCenterCode;
                    dataOutgoingPaymentDetail.CostCenterName = dataCostCenter.CostCenterName;
					dataOutgoingPaymentDetail.PostingAccountName = dataPostingAccount.CompanyName;
                    dataOutgoingPaymentDetail.OutgoingPaymentDetailAttachments.AddRange(outgoingPaymentDetailInput.OutgoingPaymentDetailAttachments.Select(d => _mapper.Map<OutgoingPaymentDetailAttachment>(d)));

					await _dbContext.AddAsync(dataOutgoingPaymentDetail);
                }
			}
			#endregion

			_dbContext.OutgoingPayments.Update(existOutgoing);

			if (input.IsSubmit)
			{
                var isSuccessWorkflow = await CreateApprovalWorkflow(existOutgoing, currentUserAccessor);

                if (!isSuccessWorkflow)
                    return new ResponseBase("Gagal membuat workflow approval", ResponseCode.Error);
            }

            await _dbContext.SaveChangesAsync();

            return new ResponseBase($"Data outgoing payment berhasil {(input.IsSubmit ? "disubmit" : "disimpan sebagai draft")}", ResponseCode.Ok);
        }

		public async Task<ResponseBase> DeleteOutgoingPayment(Guid outgoingPaymentId)
		{
			var outgoingPayment = await _dbContext.OutgoingPayments.FirstOrDefaultAsync(d => d.Id.Equals(outgoingPaymentId));
			if (outgoingPayment == null)
				return new ResponseBase("Gagal hapus data, karena data tidak ditemukan", ResponseCode.NotFound);

			if (outgoingPayment.ApprovalStatus != ApprovalStatus.Draft)
				return new ResponseBase("Gagal hapus data, dokumen yang dapat dihapus hanya dokumen berstatus draft", ResponseCode.Error);

			outgoingPayment.RowStatus = 1;
			_dbContext.Update(outgoingPayment);
			await _dbContext.SaveChangesAsync();

			return new ResponseBase("Berhasil menghapus data", ResponseCode.Ok);
		}
		#endregion

		public async Task<bool> UpdateApprovalStatusOutgoingPayment(string transactionNo, ApprovalStatus approvalStatus)
		{
			var outgoingPayment = await _dbContext.OutgoingPayments.FirstOrDefaultAsync(d => d.TransactionNo.Equals(transactionNo));
			if (outgoingPayment == null) return false;

			outgoingPayment.ApprovalStatus = approvalStatus;
			_dbContext.Update(outgoingPayment);
			await _dbContext.SaveChangesAsync();

			return true;
		}

        #region private service
        private async Task<bool> CreateApprovalWorkflow(DataAccess.Models.OutgoingPayment input, CurrentUserAccessor currentUserAccessor)
		{
			var dataRoleCodes = await _dbContext.UserRoles.Include(ur => ur.Role).Where(d => d.UserId.Equals(currentUserAccessor.Id)).Select(d => d.Role.RoleCode).ToListAsync();
			var workflowRule = await _dbContext.ApprovalRules.AsNoTracking()
				.FirstOrDefaultAsync(x =>
					x.MinAmount <= input.TotalAmount
					&& input.TotalAmount <= x.MaxAmount
					&& x.Level == 1
					&& dataRoleCodes.Contains(x.RoleCode));

			if (workflowRule == null)
				return false;

			var firstRoleApprover = await _dbContext.ApprovalRules.AsNoTracking()
				.FirstOrDefaultAsync(x => x.MinAmount == workflowRule.MinAmount && x.MaxAmount == workflowRule.MaxAmount && x.Level == 2);

			if (firstRoleApprover == null)
				return false;

			var dataRole = await _dbContext.Roles.FirstAsync(x => x.RoleCode == workflowRule.RoleCode);

			if (dataRole == null)
				return false;

			var dataInbox = new ApprovalInbox
			{
				ApprovalLevel = 2,
				ApprovalRoleCode = firstRoleApprover.RoleCode,
				ApprovalStatus = ApprovalStatus.WaitingApproval,
				TransactionNo = input.TransactionNo,
				MinAmount = workflowRule.MinAmount,
				MaxAmount = workflowRule.MaxAmount
			};

			await _dbContext.ApprovalInboxes.AddAsync(dataInbox);

			var dataHistory = new ApprovalHistory
			{
				ApprovalLevel = 1,
				ExecutorName = input.Requestor,
				ExecutorRoleCode = workflowRule.RoleCode,
				ExecutorRoleDesc = dataRole.RoleDescription,
				ApprovalStatus = ApprovalStatus.WaitingApproval,
				ApprovalUserId = currentUserAccessor.Id,
				TransactionNo = input.TransactionNo,
				MinAmount = workflowRule.MinAmount,
				MaxAmount = workflowRule.MaxAmount
			};

			await _dbContext.ApprovalHistories.AddAsync(dataHistory);

			return true;
		}

		private async Task<DocNumberConfig> GetRunningNumberDocument(string transactionTypeCode, string companyCode, DateTime date)
		{
            DocNumberConfig? docNumberConfig = null;
			int month = date.Month;
			int year = date.Year;

            docNumberConfig = await _dbContext.DocNumberConfigs
                .FirstOrDefaultAsync(x => x.TransactionTypeCode == transactionTypeCode && x.CompanyCode == companyCode && x.Month == month && x.Year == year);


            if (docNumberConfig == null)
            {
                docNumberConfig = new DocNumberConfig
                {
                    TransactionTypeCode = transactionTypeCode,
                    CompanyCode = companyCode,
                    Month = month,
                    Year = year,
                    RunningNumber = 0
                };

                await _dbContext.DocNumberConfigs.AddAsync(docNumberConfig);
                await _dbContext.SaveChangesAsync();

                return docNumberConfig;
            }
            else
            {
                return docNumberConfig;
            }
        }

        private async Task<DocNumberConfig?> UpdateRunningNumber(Guid idData)
        {
            var docNumberConfig = await _dbContext.DocNumberConfigs
                .FirstOrDefaultAsync(x => x.Id == idData);

            if (docNumberConfig == null)
                return null;

            docNumberConfig.RunningNumber = docNumberConfig.RunningNumber + 1;
            _dbContext.DocNumberConfigs.Update(docNumberConfig);
            await _dbContext.SaveChangesAsync();

            return docNumberConfig;
        }
        #endregion
    }
}
