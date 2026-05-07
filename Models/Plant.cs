using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Plantagotchi.Models;

public class Plant
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int WateringIntervalInDays { get; set; }
    public DateTime LastWatered { get; set; }
    public string? ScientificName { get; set; }
    public string? CareGuide { get; set; }
    public string? LastDiagnosis { get; set; }
    public DateTime? LastDiagnosisDate { get; set; }
    public ObservableCollection<PlantImage> Images { get; set; } = new();

    public bool NeedsWatering => (DateTime.Now - LastWatered).TotalDays >= WateringIntervalInDays;
    public DateTime NextWateringDate => LastWatered.AddDays(WateringIntervalInDays);
    public string? LatestImagePath => Images.Count > 0 ? Images.OrderByDescending(i => i.UploadDate).First().ImagePath : null;

    // Not stored in the database, only exists at runtime
    [NotMapped]
    public string? WeatherAlertMessage { get; set; }

    [NotMapped]
    public bool HasWeatherAlert => !string.IsNullOrEmpty(WeatherAlertMessage);
}