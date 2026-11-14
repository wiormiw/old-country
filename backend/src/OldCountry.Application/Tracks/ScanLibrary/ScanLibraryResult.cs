using OldCountry.Domain.Errors;

namespace OldCountry.Application.Tracks.ScanLibrary;

public readonly union ScanLibraryResult(ScanReport, ValidationError);
