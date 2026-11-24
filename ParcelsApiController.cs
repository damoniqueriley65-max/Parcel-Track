using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParcelTrack.Data;
using ParcelTrack.Filters;
using ParcelTrack.Models;
using ParcelTrack.Services;

namespace ParcelTrack.Controllers.Api
{
    // The same parcels as ParcelsController, sent as JSON instead of HTML pages.
    // [ApiController]: bad input gets an automatic 400, and an object parameter is read from the body.
    // ControllerBase, not Controller: an API has no views.
    //
    // Secured with tokens (Part A): both GETs stay open to everyone; POST and PUT need a
    // token with Admin or Staff (the CanManageParcels policy); DELETE needs an Admin token.
    // A write with no token gives 401, never a 302. A Staff token on DELETE gives 403.
    [ApiController]
    [Route("api/parcels")]
    public class ParcelsApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IDeliveryFeeCalculator _feeCalculator;

        // Part D: the fee rule is injected, never new'd here.
        public ParcelsApiController(AppDbContext db, IDeliveryFeeCalculator feeCalculator)
        {
            _db = db;
            _feeCalculator = feeCalculator;
        }

        // Turns one Parcel (a database row) into one ParcelDto (what the API sends).
        // Includes the delivery fee (Part D) in every reply.
        private ParcelDto ToDto(Parcel parcel)
        {
            ParcelDto dto = new ParcelDto();
            dto.Id = parcel.Id;
            dto.TrackingCode = parcel.TrackingCode;
            dto.Recipient = parcel.Recipient;
            dto.WeightKg = parcel.WeightKg;
            dto.Status = parcel.Status;
            dto.BranchId = parcel.BranchId;
            if (parcel.Branch != null)
            {
                dto.BranchName = parcel.Branch.Name;
                // The fee depends on the weight and the branch's parish.
                dto.DeliveryFee = _feeCalculator.Calculate(parcel.WeightKg, parcel.Branch.Parish);
            }
            else
            {
                // No branch loaded: fall back to no surcharge (the fee still has the base + per-kg).
                dto.DeliveryFee = _feeCalculator.Calculate(parcel.WeightKg, "");
            }
            return dto;
        }

        // GET /api/parcels: every parcel, sorted by tracking code. 200 and a JSON list.
        // Open to everyone.
        [AllowAnonymous]
        [HttpGet]
        public IActionResult GetAll()
        {
            List<Parcel> parcels = _db.Parcels
                .Include(parcel => parcel.Branch)
                .OrderBy(parcel => parcel.TrackingCode)
                .ToList();

            List<ParcelDto> result = new List<ParcelDto>();
            foreach (Parcel parcel in parcels)
            {
                result.Add(ToDto(parcel));
            }
            return Ok(result);
        }

        // GET /api/parcels/3: one parcel. 200, or 404 if there is no such Id.
        // Open to everyone.
        [AllowAnonymous]
        [HttpGet("{id:int}")]
        public IActionResult GetOne(int id)
        {
            Parcel? parcel = _db.Parcels
                .Include(each_parcel => each_parcel.Branch)
                .FirstOrDefault(each_parcel => each_parcel.Id == id);
            if (parcel == null)
            {
                return NotFound();
            }
            return Ok(ToDto(parcel));
        }

        // POST /api/parcels with a JSON body: add a parcel.
        // 201 Created + a Location header, 400 if a rule fails, 409 if the tracking code is taken.
        // Needs a token with Admin or Staff (CanManageParcels), and an X-Client header (Part C).
        [Authorize(Policy = "CanManageParcels")]
        [RequireClient]
        [HttpPost]
        public IActionResult Create(ParcelInput input)
        {
            Parcel parcel = new Parcel();
            parcel.TrackingCode = input.TrackingCode;
            parcel.Recipient = input.Recipient;
            parcel.WeightKg = input.WeightKg;
            parcel.Status = input.Status;
            parcel.BranchId = input.BranchId;

            _db.Parcels.Add(parcel);
            try
            {
                _db.SaveChanges();
            }
            catch (DbUpdateException)
            {
                // The unique index said no (or the branchId doesn't exist)
                return Conflict(new { message = "Another parcel already has that tracking code, or there is no such branch" });
            }

            // Fetch the branch, so the reply can include its name and the fee
            _db.Entry(parcel).Reference(saved_parcel => saved_parcel.Branch).Load();

            return CreatedAtAction(nameof(GetOne), new { id = parcel.Id }, ToDto(parcel));
        }

        // PUT /api/parcels/2 with a JSON body: replace parcel 2's details.
        // 204 No Content, 404 if there is no parcel 2, 400 if a rule fails, 409 if the code is another parcel's.
        // Needs a token with Admin or Staff (CanManageParcels), and an X-Client header (Part C).
        [Authorize(Policy = "CanManageParcels")]
        [RequireClient]
        [HttpPut("{id:int}")]
        public IActionResult Update(int id, ParcelInput input)
        {
            Parcel? parcel = _db.Parcels.Find(id);
            if (parcel == null)
            {
                return NotFound();
            }
            parcel.TrackingCode = input.TrackingCode;
            parcel.Recipient = input.Recipient;
            parcel.WeightKg = input.WeightKg;
            parcel.Status = input.Status;
            parcel.BranchId = input.BranchId;

            try
            {
                _db.SaveChanges();
            }
            catch (DbUpdateException)
            {
                return Conflict(new { message = "Another parcel already has that tracking code, or there is no such branch" });
            }
            return NoContent();
        }

        // DELETE /api/parcels/5: 204 No Content, or 404 if it is already gone.
        // Needs an Admin token, and an X-Client header (Part C).
        [Authorize(Roles = "Admin")]
        [RequireClient]
        [HttpDelete("{id:int}")]
        public IActionResult Delete(int id)
        {
            Parcel? parcel = _db.Parcels.Find(id);
            if (parcel == null)
            {
                return NotFound();
            }
            _db.Parcels.Remove(parcel);
            _db.SaveChanges();
            return NoContent();
        }
    }
}
