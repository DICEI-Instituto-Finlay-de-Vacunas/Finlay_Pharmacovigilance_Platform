

using Finlay.PharmaVigilance.Application.DTO;
using Finlay.PharmaVigilance.Application.IRepository;
using Finlay.PharmaVigilance.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finlay.PharmaVigilance.Infrastructure.Repository;

public class VaccinationRepository : GenericRepository<Vaccination>, IVaccinationRepository
{
    public VaccinationRepository(FinlayDbContext context) : base(context) { }


    public async Task<IEnumerable<VaccineStatsDto>> GetVaccineByFilter(int municipalityId)
    {
        var result = await _entity
            .Include(vac => vac.AefiReport)
            .Include(vac => vac.Lot)
                .ThenInclude(l => l.Vaccine)
            .Where(x => x.AefiReport.VaccinatedSubject.MunicipalityId == municipalityId)
            .GroupBy(vac => vac.Lot.Vaccine.Name)
            .Select(g => new VaccineStatsDto
            {
                VaccineName = g.Key,
                TotalReports = g.Select(vac => vac.AefiReport.Id).Distinct().Count()
            })
            .ToListAsync();

        foreach (var i in result)
        {
            Console.WriteLine($"{i.VaccineName} ---> {i.TotalReports}");
        }


        return result;

    }

    public async Task<IEnumerable<VaccineStatusDto>> GetVaccineDistributionAsync()
    {
        // Two flat GROUP BY queries instead of one nested grouping: EF Core translates the nested
        // version to OUTER APPLY, which MySQL/TiDB cannot run. The lots are attached in memory.
        var vaccinations = _context.AefiReport.SelectMany(r => r.Vaccinations);

        var perVaccine = await vaccinations
            .GroupBy(v => new { v.Lot.Vaccine.Id, v.Lot.Vaccine.Name })
            .Select(g => new
            {
                g.Key.Id,
                g.Key.Name,
                TotalReports = g.Select(v => v.AefiReportId).Distinct().Count()
            })
            .ToListAsync();

        var perLot = await vaccinations
            .GroupBy(v => new { VaccineId = v.Lot.Vaccine.Id, LotId = v.Lot.Id, v.Lot.LotNumber })
            .Select(g => new
            {
                g.Key.VaccineId,
                g.Key.LotNumber,
                TotalReports = g.Select(v => v.AefiReportId).Distinct().Count()
            })
            .ToListAsync();

        var result = perVaccine
            .Select(v => new VaccineStatusDto
            {
                VaccineName = v.Name,
                TotalReports = v.TotalReports,
                Lots = perLot
                    .Where(l => l.VaccineId == v.Id)
                    .Select(l => new LotsStatusDto { LotNumber = l.LotNumber, TotalReports = l.TotalReports })
                    .ToList()
            })
            .ToList();

        return result;
    }


}