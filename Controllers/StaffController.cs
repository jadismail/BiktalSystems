using System.Security.Claims;
using Biktal.Domain.Staff;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Biktal.WebMVC.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Controllers;

[Authorize(Roles = AppRoleGroups.Management)]
public sealed class StaffController : Controller
{
    private readonly ApplicationDbContext _db;

    public StaffController(ApplicationDbContext db)
    {
        _db = db;
    }

    private string ActorName =>
        User.FindFirstValue(ClaimTypes.Name)
        ?? User.Identity?.Name
        ?? "Staff";

    private async Task LogActivityAsync(
        StaffActivityCategory category,
        string message,
        Guid? relatedStaffMemberId = null,
        CancellationToken cancellationToken = default)
    {
        _db.StaffActivityLogEntries.Add(new StaffActivityLogEntry
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            ActorDisplayName = ActorName,
            Category = category,
            Message = message,
            RelatedStaffMemberId = relatedStaffMemberId
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var activeStaff = await _db.StaffMembers.AsNoTracking()
            .CountAsync(m => m.IsActive, cancellationToken);

        var openSessions = await _db.StaffAttendanceEntries.AsNoTracking()
            .CountAsync(e => e.ClockOutUtc == null, cancellationToken);

        var draftRuns = await _db.StaffPayrollRuns.AsNoTracking()
            .CountAsync(r => r.Status == StaffPayrollRunStatus.Draft, cancellationToken);

        var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
        var activityWeek = await _db.StaffActivityLogEntries.AsNoTracking()
            .CountAsync(a => a.OccurredAtUtc >= weekAgo, cancellationToken);

        return View(new StaffIndexViewModel
        {
            ActiveStaffCount = activeStaff,
            OpenAttendanceSessionCount = openSessions,
            DraftPayrollRunCount = draftRuns,
            ActivityEntriesLast7DaysCount = activityWeek
        });
    }

    [HttpGet]
    public async Task<IActionResult> Team(CancellationToken cancellationToken)
    {
        var rows = await _db.StaffMembers.AsNoTracking()
            .OrderBy(m => m.DisplayName)
            .Select(m => new StaffMemberListRowViewModel
            {
                Id = m.Id,
                DisplayName = m.DisplayName,
                JobTitle = m.JobTitle,
                EmployeeCode = m.EmployeeCode,
                WorkEmail = m.WorkEmail,
                IsActive = m.IsActive
            })
            .ToListAsync(cancellationToken);

        return View(new StaffTeamPageViewModel { Members = rows });
    }

    [HttpGet]
    public IActionResult NewMember()
    {
        return View(new CreateStaffMemberFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMember(CreateStaffMemberFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("NewMember", model);

        var code = model.EmployeeCode.Trim();
        if (await _db.StaffMembers.AnyAsync(m => m.EmployeeCode == code, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.EmployeeCode), "That employee code is already in use.");
            return View("NewMember", model);
        }

        var id = Guid.NewGuid();
        _db.StaffMembers.Add(new StaffMember
        {
            Id = id,
            DisplayName = model.DisplayName.Trim(),
            JobTitle = model.JobTitle.Trim(),
            WorkEmail = string.IsNullOrWhiteSpace(model.WorkEmail) ? null : model.WorkEmail.Trim(),
            Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
            EmployeeCode = code,
            HireDate = model.HireDate,
            TerminationDate = null,
            HourlyRate = model.HourlyRate,
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
            LinkedUserId = null,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        await LogActivityAsync(StaffActivityCategory.Staff,
            $"Team member created: {model.DisplayName.Trim()} ({code}).", id, cancellationToken);
        TempData["StaffMessage"] = "Team member created.";
        return RedirectToAction(nameof(Team));
    }

    [HttpGet]
    public async Task<IActionResult> Member(Guid id, CancellationToken cancellationToken)
    {
        var m = await _db.StaffMembers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (m is null)
            return NotFound();

        var form = new EditStaffMemberFormModel
        {
            Id = m.Id,
            DisplayName = m.DisplayName,
            JobTitle = m.JobTitle,
            WorkEmail = m.WorkEmail,
            Phone = m.Phone,
            EmployeeCode = m.EmployeeCode,
            HireDate = m.HireDate,
            TerminationDate = m.TerminationDate,
            HourlyRate = m.HourlyRate,
            Notes = m.Notes
        };

        return View(new StaffMemberDetailPageViewModel { Form = form });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMember(EditStaffMemberFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Member", new StaffMemberDetailPageViewModel { Form = model });

        var m = await _db.StaffMembers.FirstOrDefaultAsync(x => x.Id == model.Id, cancellationToken);
        if (m is null)
            return NotFound();

        var code = model.EmployeeCode.Trim();
        if (await _db.StaffMembers.AnyAsync(x => x.EmployeeCode == code && x.Id != model.Id, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.EmployeeCode), "That employee code is already in use.");
            return View("Member", new StaffMemberDetailPageViewModel { Form = model });
        }

        m.DisplayName = model.DisplayName.Trim();
        m.JobTitle = model.JobTitle.Trim();
        m.WorkEmail = string.IsNullOrWhiteSpace(model.WorkEmail) ? null : model.WorkEmail.Trim();
        m.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        m.EmployeeCode = code;
        m.HireDate = model.HireDate;
        m.TerminationDate = model.TerminationDate;
        m.HourlyRate = model.HourlyRate;
        m.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
        m.ModifiedAtUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await LogActivityAsync(StaffActivityCategory.Staff,
            $"Team member updated: {m.DisplayName} ({m.EmployeeCode}).", m.Id, cancellationToken);
        TempData["StaffMessage"] = "Changes saved.";
        return RedirectToAction(nameof(Member), new { id = m.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateMember(Guid id, CancellationToken cancellationToken)
    {
        var m = await _db.StaffMembers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (m is null)
            return NotFound();

        m.IsActive = false;
        m.TerminationDate ??= DateOnly.FromDateTime(DateTime.UtcNow);
        m.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await LogActivityAsync(StaffActivityCategory.Staff,
            $"Team member deactivated: {m.DisplayName} ({m.EmployeeCode}).", m.Id, cancellationToken);
        TempData["StaffMessage"] = "Member marked inactive.";
        return RedirectToAction(nameof(Team));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivateMember(Guid id, CancellationToken cancellationToken)
    {
        var m = await _db.StaffMembers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (m is null)
            return NotFound();

        m.IsActive = true;
        m.TerminationDate = null;
        m.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await LogActivityAsync(StaffActivityCategory.Staff,
            $"Team member reactivated: {m.DisplayName} ({m.EmployeeCode}).", m.Id, cancellationToken);
        TempData["StaffMessage"] = "Member reactivated.";
        return RedirectToAction(nameof(Team));
    }

    [HttpGet]
    public async Task<IActionResult> Attendance(CancellationToken cancellationToken)
    {
        var open = await _db.StaffAttendanceEntries.AsNoTracking()
            .Where(e => e.ClockOutUtc == null)
            .Join(_db.StaffMembers.AsNoTracking(),
                e => e.StaffMemberId,
                m => m.Id,
                (e, m) => new StaffAttendanceOpenRowViewModel
                {
                    EntryId = e.Id,
                    StaffMemberId = m.Id,
                    DisplayName = m.DisplayName,
                    EmployeeCode = m.EmployeeCode,
                    ClockInUtc = e.ClockInUtc
                })
            .OrderByDescending(x => x.ClockInUtc)
            .ToListAsync(cancellationToken);

        var recent = await _db.StaffAttendanceEntries.AsNoTracking()
            .Join(_db.StaffMembers.AsNoTracking(),
                e => e.StaffMemberId,
                m => m.Id,
                (e, m) => new { e, m })
            .OrderByDescending(x => x.e.ClockInUtc)
            .Take(40)
            .Select(x => new StaffAttendanceRecentRowViewModel
            {
                EntryId = x.e.Id,
                DisplayName = x.m.DisplayName,
                EmployeeCode = x.m.EmployeeCode,
                ClockInUtc = x.e.ClockInUtc,
                ClockOutUtc = x.e.ClockOutUtc,
                Notes = x.e.Notes
            })
            .ToListAsync(cancellationToken);

        var picks = await _db.StaffMembers.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayName)
            .Select(m => new StaffMemberPickOptionViewModel
            {
                Id = m.Id,
                Label = m.DisplayName + " (" + m.EmployeeCode + ")"
            })
            .ToListAsync(cancellationToken);

        return View(new StaffAttendancePageViewModel
        {
            OpenSessions = open,
            RecentEntries = recent,
            ActiveMembersForClockIn = picks,
            ClockInForm = new StaffClockInFormModel()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClockIn([Bind(Prefix = "ClockInForm")] StaffClockInFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["StaffError"] = "Fix clock-in details and try again.";
            return RedirectToAction(nameof(Attendance));
        }

        var member = await _db.StaffMembers.FirstOrDefaultAsync(m => m.Id == model.StaffMemberId, cancellationToken);
        if (member is null || !member.IsActive)
        {
            TempData["StaffError"] = "That team member was not found or is inactive.";
            return RedirectToAction(nameof(Attendance));
        }

        var open = await _db.StaffAttendanceEntries.AnyAsync(
            e => e.StaffMemberId == model.StaffMemberId && e.ClockOutUtc == null,
            cancellationToken);

        if (open)
        {
            TempData["StaffError"] = $"{member.DisplayName} already has an open clock-in. Clock out first.";
            return RedirectToAction(nameof(Attendance));
        }

        _db.StaffAttendanceEntries.Add(new StaffAttendanceEntry
        {
            Id = Guid.NewGuid(),
            StaffMemberId = model.StaffMemberId,
            ClockInUtc = DateTimeOffset.UtcNow,
            ClockOutUtc = null,
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim()
        });

        await _db.SaveChangesAsync(cancellationToken);
        await LogActivityAsync(StaffActivityCategory.Staff,
            $"Clock in: {member.DisplayName} ({member.EmployeeCode}).", member.Id, cancellationToken);
        TempData["StaffMessage"] = "Clocked in.";
        return RedirectToAction(nameof(Attendance));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClockOut(StaffClockOutFormModel model, CancellationToken cancellationToken)
    {
        var entry = await _db.StaffAttendanceEntries
            .Include(e => e.StaffMember)
            .FirstOrDefaultAsync(e => e.Id == model.AttendanceEntryId, cancellationToken);

        if (entry is null || entry.ClockOutUtc != null)
        {
            TempData["StaffError"] = "That open session was not found.";
            return RedirectToAction(nameof(Attendance));
        }

        entry.ClockOutUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var name = entry.StaffMember?.DisplayName ?? "Team member";
        var code = entry.StaffMember?.EmployeeCode ?? "";
        await LogActivityAsync(StaffActivityCategory.Staff,
            $"Clock out: {name} ({code}).", entry.StaffMemberId, cancellationToken);
        TempData["StaffMessage"] = "Clocked out.";
        return RedirectToAction(nameof(Attendance));
    }

    [HttpGet]
    public async Task<IActionResult> Payroll(CancellationToken cancellationToken)
    {
        var runs = await _db.StaffPayrollRuns.AsNoTracking()
            .OrderByDescending(r => r.PeriodEnd)
            .Select(r => new StaffPayrollRunListRowViewModel
            {
                Id = r.Id,
                Label = r.Label,
                PeriodStart = r.PeriodStart,
                PeriodEnd = r.PeriodEnd,
                Status = r.Status,
                LineCount = r.Lines.Count,
                TotalNetPay = r.Lines.Sum(l => l.NetPay)
            })
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = new DateOnly(today.Year, today.Month, 1);
        var end = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        return View(new StaffPayrollListPageViewModel
        {
            Runs = runs,
            NewRunForm = new CreateStaffPayrollRunFormModel
            {
                Label = $"{today:MMMM yyyy} payroll",
                PeriodStart = start,
                PeriodEnd = end
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePayrollRun([Bind(Prefix = "NewRunForm")] CreateStaffPayrollRunFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["StaffError"] = "Check payroll period fields.";
            return RedirectToAction(nameof(Payroll));
        }

        if (model.PeriodEnd < model.PeriodStart)
        {
            TempData["StaffError"] = "Period end must be on or after period start.";
            return RedirectToAction(nameof(Payroll));
        }

        var overlap = await _db.StaffPayrollRuns.AnyAsync(
            r => r.PeriodStart == model.PeriodStart && r.PeriodEnd == model.PeriodEnd,
            cancellationToken);

        if (overlap)
        {
            TempData["StaffError"] = "A payroll run already exists for that exact period.";
            return RedirectToAction(nameof(Payroll));
        }

        var members = await _db.StaffMembers
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayName)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (members.Count == 0)
        {
            TempData["StaffError"] = "Add active team members before creating a payroll run.";
            return RedirectToAction(nameof(Payroll));
        }

        var runId = Guid.NewGuid();
        var run = new StaffPayrollRun
        {
            Id = runId,
            Label = model.Label.Trim(),
            PeriodStart = model.PeriodStart,
            PeriodEnd = model.PeriodEnd,
            Status = StaffPayrollRunStatus.Draft,
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        foreach (var mid in members)
        {
            run.Lines.Add(new StaffPayrollLine
            {
                Id = Guid.NewGuid(),
                StaffPayrollRunId = runId,
                StaffMemberId = mid,
                GrossPay = 0,
                Deductions = 0,
                NetPay = 0
            });
        }

        _db.StaffPayrollRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);
        await LogActivityAsync(StaffActivityCategory.Staff,
            $"Payroll run created: {run.Label} ({run.PeriodStart:yyyy-MM-dd} → {run.PeriodEnd:yyyy-MM-dd}).", null,
            cancellationToken);
        TempData["StaffMessage"] = "Payroll run created with one line per active member.";
        return RedirectToAction(nameof(PayrollRun), new { id = runId });
    }

    [HttpGet]
    public async Task<IActionResult> PayrollRun(Guid id, CancellationToken cancellationToken)
    {
        var run = await _db.StaffPayrollRuns.AsNoTracking()
            .Include(r => r.Lines)
            .ThenInclude(l => l.StaffMember)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (run is null)
            return NotFound();

        var vm = new StaffPayrollRunDetailPageViewModel
        {
            RunId = run.Id,
            Label = run.Label,
            PeriodStart = run.PeriodStart,
            PeriodEnd = run.PeriodEnd,
            Status = run.Status,
            Notes = run.Notes,
            CanEditLines = run.Status == StaffPayrollRunStatus.Draft,
            CanAdvanceStatus = run.Status != StaffPayrollRunStatus.Paid,
            Lines = run.Lines
                .OrderBy(l => l.StaffMember!.DisplayName)
                .Select(l => new StaffPayrollLineEditRowViewModel
                {
                    LineId = l.Id,
                    MemberName = l.StaffMember!.DisplayName,
                    EmployeeCode = l.StaffMember.EmployeeCode,
                    GrossPay = l.GrossPay,
                    Deductions = l.Deductions,
                    NetPay = l.NetPay
                })
                .ToList()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePayrollLine(Guid runId, UpdateStaffPayrollLineFormModel model, CancellationToken cancellationToken)
    {
        var line = await _db.StaffPayrollLines
            .Include(l => l.PayrollRun)
            .Include(l => l.StaffMember)
            .FirstOrDefaultAsync(l => l.Id == model.LineId && l.StaffPayrollRunId == runId, cancellationToken);

        if (line is null)
            return NotFound();

        if (line.PayrollRun.Status != StaffPayrollRunStatus.Draft)
        {
            TempData["StaffError"] = "Only draft payroll runs can be edited.";
            return RedirectToAction(nameof(PayrollRun), new { id = runId });
        }

        if (!ModelState.IsValid)
        {
            TempData["StaffError"] = "Invalid amounts.";
            return RedirectToAction(nameof(PayrollRun), new { id = runId });
        }

        var gross = decimal.Round(model.GrossPay, 2, MidpointRounding.AwayFromZero);
        var ded = decimal.Round(model.Deductions, 2, MidpointRounding.AwayFromZero);
        if (ded > gross)
        {
            TempData["StaffError"] = "Deductions cannot exceed gross pay.";
            return RedirectToAction(nameof(PayrollRun), new { id = runId });
        }

        line.GrossPay = gross;
        line.Deductions = ded;
        line.NetPay = gross - ded;
        line.PayrollRun.ModifiedAtUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await LogActivityAsync(StaffActivityCategory.Staff,
            $"Payroll line updated for {line.StaffMember.DisplayName} on run {line.PayrollRun.Label}.", line.StaffMemberId,
            cancellationToken);
        TempData["StaffMessage"] = "Payroll line saved.";
        return RedirectToAction(nameof(PayrollRun), new { id = runId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvancePayrollRun(Guid id, CancellationToken cancellationToken)
    {
        var run = await _db.StaffPayrollRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
            return NotFound();

        if (run.Status == StaffPayrollRunStatus.Draft)
            run.Status = StaffPayrollRunStatus.Submitted;
        else if (run.Status == StaffPayrollRunStatus.Submitted)
            run.Status = StaffPayrollRunStatus.Paid;
        else
        {
            TempData["StaffError"] = "This run is already marked paid.";
            return RedirectToAction(nameof(PayrollRun), new { id });
        }

        run.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await LogActivityAsync(StaffActivityCategory.Staff,
            $"Payroll run status advanced to {StaffPayrollRunStatusLabels.Title(run.Status)}: {run.Label}.", null,
            cancellationToken);
        TempData["StaffMessage"] = "Payroll status updated.";
        return RedirectToAction(nameof(PayrollRun), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Activity(CancellationToken cancellationToken)
    {
        var names = await _db.StaffMembers.AsNoTracking()
            .ToDictionaryAsync(m => m.Id, m => m.DisplayName, cancellationToken);

        var list = await _db.StaffActivityLogEntries.AsNoTracking()
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        var resolved = list.Select(a => new StaffActivityRowViewModel
        {
            OccurredAtUtc = a.OccurredAtUtc,
            ActorDisplayName = a.ActorDisplayName,
            Category = a.Category,
            Message = a.Message,
            RelatedStaffMemberId = a.RelatedStaffMemberId,
            RelatedMemberName = a.RelatedStaffMemberId is { } rid && names.TryGetValue(rid, out var n) ? n : null
        }).ToList();

        return View(new StaffActivityPageViewModel { Entries = resolved });
    }
}
