using System;

namespace Plantagotchi.Models;

public class PlantImage
{
    public int Id { get; set; }
    public int PlantId { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public Plant? Plant { get; set; }
}