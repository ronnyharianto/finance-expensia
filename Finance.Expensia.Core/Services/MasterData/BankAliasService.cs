﻿using AutoMapper;
using Finance.Expensia.Core.Services.MasterData.Dtos;
using Finance.Expensia.Core.Services.MasterData.Inputs;
using Finance.Expensia.DataAccess;
using Finance.Expensia.DataAccess.Models;
using Finance.Expensia.Shared.Enums;
using Finance.Expensia.Shared.Objects.Dtos;
using Finance.Expensia.Shared.Objects.Inputs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Finance.Expensia.Core.Services.MasterData
{
    public class BankAliasService(ApplicationDbContext dbContext, IMapper mapper, ILogger<BankAliasService> logger)
        : BaseService<BankAliasService>(dbContext, mapper, logger)
    {
        public async Task<ResponseObject<List<BankAliasDto>>> RetrieveBankAlias(Guid? companyId)
        {
            var dataBankAliases = await _dbContext.BankAliases
                                                  .Where(d => companyId == null || d.CompanyId.Equals(companyId.Value))
                                                  .OrderBy(d => d.AliasName)
                                                  .Select(d => _mapper.Map<BankAliasDto>(d))
                                                  .ToListAsync();

            return new ResponseObject<List<BankAliasDto>>(responseCode: ResponseCode.Ok)
            {
                Obj = dataBankAliases
            };
        }

        public async Task<ResponseObject<List<BankAliasDto>>> RetrieveFromBankAlias(Guid? companyId)
        {
            var dataBankAliases = await _dbContext.BankAliases
                                                  .Where(d => companyId == null || d.CompanyId.Equals(companyId.Value))
                                                  .Where(d => d.CompanyId.HasValue)
                                                  .OrderBy(d => d.AliasName)
                                                  .Select(d => _mapper.Map<BankAliasDto>(d))
                                                  .ToListAsync();

            return new ResponseObject<List<BankAliasDto>>(responseCode: ResponseCode.Ok)
            {
                Obj = dataBankAliases
            };
        }

        public async Task<ResponsePaging<BankAliasDto>> GetListBankAlias(PagingSearchInputBase input)
        {
            var retVal = new ResponsePaging<BankAliasDto>();
            var dataBankAliases = _dbContext.BankAliases
                                            .Include(d => d.Company)
                                            .Where(d => 
                                                (d.Company != null && EF.Functions.Like(d.Company.CompanyName, $"%{input.SearchKey}%"))
                                                || EF.Functions.Like(d.AliasName, $"%{input.SearchKey}%")
                                                || EF.Functions.Like(d.BankName, $"%{input.SearchKey}%")
                                                || EF.Functions.Like(d.AccountNo, $"%{input.SearchKey}%")
                                                || EF.Functions.Like(d.AccountName, $"%{input.SearchKey}%"))
                                            .OrderByDescending(d => d.Modified ?? d.Created)
                                            .Select(d => _mapper.Map<BankAliasDto>(d));

            retVal.ApplyPagination(input.Page, input.PageSize, dataBankAliases);

            return await Task.FromResult(retVal);
        }

        public async Task<ResponseObject<BankAliasDto>> GetDetailBankAlias(Guid bankAliasId)
        {
            var dataBankAlias = await _dbContext.BankAliases
                                                .Include(d => d.Company)    
                                                .FirstOrDefaultAsync(x => x.Id == bankAliasId);

            if (dataBankAlias == null)
                return new ResponseObject<BankAliasDto>("Data bank alias tidak ditemukan", ResponseCode.NotFound);

            return new ResponseObject<BankAliasDto>(responseCode: ResponseCode.Ok)
            {
                Obj = _mapper.Map<BankAliasDto>(dataBankAlias),
            };
        }

        public async Task<ResponseBase> UpsertBankAlias(UpsertBankAliasInput input)
        {
            if (input.Id.Equals(null))
            {
                var dataBankAlias = _mapper.Map<BankAlias>(input);
                await _dbContext.AddAsync(dataBankAlias);
            }
            else
            {
                var dataBankAlias = await _dbContext.BankAliases.FirstOrDefaultAsync(v => v.Id.Equals(input.Id));
                if (dataBankAlias == null)
                    return new ResponseBase("Data bank alias tidak ditemukan", ResponseCode.NotFound);

                _mapper.Map(input, dataBankAlias);
                _dbContext.Update(dataBankAlias);
            }

            await _dbContext.SaveChangesAsync();
            return new ResponseBase($"Data bank alias berhasil {(input.Id.Equals(null) ? "dibuat" : "diedit")}", ResponseCode.Ok);
        }

        public async Task<ResponseBase> DeleteBankAlias(Guid bankAliasId)
        {
            var dataBankAlias = await _dbContext.BankAliases.FirstOrDefaultAsync(v => v.Id.Equals(bankAliasId));
            if (dataBankAlias == null)
                return new ResponseBase("Data bank alias tidak ditemukan", ResponseCode.NotFound);

            dataBankAlias.RowStatus = 1;
            _dbContext.Update(dataBankAlias);
            await _dbContext.SaveChangesAsync();

            return new ResponseBase("Data bank alias berasil dihapus", ResponseCode.Ok);
        }
    }
}
