using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using Plantagotchi.Data;
using Plantagotchi.Models;
using Plantagotchi.Services;
using System.Threading.Tasks;
using System.Threading;

namespace Plantagotchi.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly SemaphoreSlim _dbSemaphore = new SemaphoreSlim(1, 1);
    private readonly PlantApiService _apiService;

    public ObservableCollection<Plant> Plants { get; set; }
    public ICollectionView PlantsView { get; private set; }

    private string _searchText = string.Empty;
    public string SearchText { get => _searchText; set { _searchText = value; OnPropertyChanged(); PlantsView.Refresh(); } }

    private string _newPlantName = string.Empty;
    public string NewPlantName { get => _newPlantName; set { _newPlantName = value; OnPropertyChanged(); } }

    private bool _isSearching;
    public bool IsSearching { get => _isSearching; set { _isSearching = value; OnPropertyChanged(); } }

    private bool _isDiagnosing;
    public bool IsDiagnosing { get => _isDiagnosing; set { _isDiagnosing = value; OnPropertyChanged(); } }

    private string _currentWeather = "Loading...";
    public string CurrentWeather { get => _currentWeather; set { _currentWeather = value; OnPropertyChanged(); } }

    public ObservableCollection<PlantWeatherAlert> WeatherAlerts { get; } = new();
    public bool ShowWeatherAlerts { get; private set; }
    public bool ShowPositiveWeatherMessage { get; private set; }

    public bool IsEditDialogOpen { get; set; }
    public bool IsDetailsDialogOpen { get; set; }
    public Plant? SelectedPlantForDetails { get; set; }

    public Plant? SelectedPlantForEdit { get; set; }
    private string _editPlantName = string.Empty;
    public string EditPlantName { get => _editPlantName; set { _editPlantName = value; OnPropertyChanged(); } }

    public ICommand AddPlantCommand { get; }
    public ICommand WaterPlantCommand { get; }
    public ICommand DeletePlantCommand { get; }
    public ICommand UploadImageCommand { get; }
    public ICommand OpenDetailsDialogCommand { get; }
    public ICommand CloseDetailsDialogCommand { get; }
    public ICommand DiagnosePlantCommand { get; }
    public ICommand OpenEditDialogCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }

    public ICommand ForceRefreshWeatherCommand { get; } // Manual AI refresh command

    public MainViewModel()
    {
        _apiService = new PlantApiService();
        Plants = new ObservableCollection<Plant>();

        PlantsView = CollectionViewSource.GetDefaultView(Plants);
        PlantsView.Filter = obj => obj is Plant p && (string.IsNullOrEmpty(SearchText) || p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        AddPlantCommand = new RelayCommand(AddPlant);
        WaterPlantCommand = new RelayCommand(WaterPlant);
        DeletePlantCommand = new RelayCommand(DeletePlant);
        UploadImageCommand = new RelayCommand(UploadImage);
        OpenDetailsDialogCommand = new RelayCommand(p => { if (p is Plant plant) { SelectedPlantForDetails = plant; IsDetailsDialogOpen = true; OnPropertyChanged(nameof(IsDetailsDialogOpen)); OnPropertyChanged(nameof(SelectedPlantForDetails)); } });
        CloseDetailsDialogCommand = new RelayCommand(p => { IsDetailsDialogOpen = false; OnPropertyChanged(nameof(IsDetailsDialogOpen)); });
        DiagnosePlantCommand = new RelayCommand(DiagnosePlant);
        OpenEditDialogCommand = new RelayCommand(OpenEditDialog);
        SaveEditCommand = new RelayCommand(SaveEdit);
        CancelEditCommand = new RelayCommand(CancelEdit);

        ForceRefreshWeatherCommand = new RelayCommand(async _ => await FetchWeatherAsync(true)); // When the refresh button is pressed, we force the query
    }

    // --- CACHE LOGIC ---
    private record AlertsCache(DateTime SavedOn, List<PlantWeatherAlert> Alerts);
    private const string AlertsCacheFile = "weather_alerts_cache.json";

    private bool ShouldRefreshAlerts()
    {
        if (!File.Exists(AlertsCacheFile)) return true;
        try
        {
            var cache = JsonSerializer.Deserialize<AlertsCache>(File.ReadAllText(AlertsCacheFile));
            if (cache == null) return true;
            // If the save was more than 7 days ago, we refresh!
            if ((DateTime.Now - cache.SavedOn).TotalDays >= 7) return true;
            return false;
        }
        catch { return true; }
    }

    private List<PlantWeatherAlert> LoadCachedAlerts()
    {
        try
        {
            var cache = JsonSerializer.Deserialize<AlertsCache>(File.ReadAllText(AlertsCacheFile));
            return cache?.Alerts ?? new();
        }
        catch { return new(); }
    }

    private void SaveAlertsCache(List<PlantWeatherAlert> alerts)
    {
        try
        {
            var cache = new AlertsCache(DateTime.Now, alerts);
            File.WriteAllText(AlertsCacheFile, JsonSerializer.Serialize(cache));
        }
        catch { /* Silent fail */ }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using (var dbContext = new AppDbContext())
                {
                    await dbContext.Database.EnsureCreatedAsync();
                    var plantList = await dbContext.Plants.Include(p => p.Images).ToListAsync();
                    foreach (var plant in plantList) Plants.Add(plant);
                }
            }
            finally { _dbSemaphore.Release(); }

            await FetchWeatherAsync(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup error: {ex.Message}");
        }
    }

    private async Task FetchWeatherAsync(bool forceRefresh)
    {
        try
        {
            IsSearching = true; // Spin the loading icon while working!

            // Always fetch the current temperature (free API) fresh
            var (currentDisplay, forecastForAI) = await _apiService.GetWeatherAndForecastAsync();
            CurrentWeather = currentDisplay;

            if (string.IsNullOrWhiteSpace(forecastForAI)) return;

            var plantNames = Plants.Select(p => p.Name).ToList();
            if (!plantNames.Any()) { ShowPositiveWeatherMessage = true; OnPropertyChanged(nameof(ShowPositiveWeatherMessage)); return; }

            List<PlantWeatherAlert> alerts;

            // If a forced refresh was requested, OR the 7-day limit has expired
            if (forceRefresh || ShouldRefreshAlerts())
            {
                alerts = await _apiService.GetPlantSpecificWeatherAlertsAsync(forecastForAI, string.Join(", ", plantNames));
                SaveAlertsCache(alerts); // Save the response
            }
            else
            {
                // Otherwise, we load it from the file for free!
                alerts = LoadCachedAlerts();
            }

            // Map them onto the plants
            foreach (var plant in Plants)
            {
                var match = alerts.FirstOrDefault(a => a.PlantName.Equals(plant.Name, StringComparison.OrdinalIgnoreCase));
                plant.WeatherAlertMessage = match?.AlertMessage;
            }
            PlantsView.Refresh();

            WeatherAlerts.Clear();
            foreach (var alert in alerts) WeatherAlerts.Add(alert);

            ShowWeatherAlerts = alerts.Any();
            ShowPositiveWeatherMessage = !alerts.Any();

            OnPropertyChanged(nameof(ShowWeatherAlerts));
            OnPropertyChanged(nameof(ShowPositiveWeatherMessage));
        }
        catch (Exception ex)
        {
            ShowWeatherAlerts = false;
            ShowPositiveWeatherMessage = false;
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async void AddPlant(object? p)
    {
        if (string.IsNullOrWhiteSpace(NewPlantName)) return;
        try
        {
            var plant = new Plant { Name = NewPlantName, WateringIntervalInDays = 7, LastWatered = DateTime.Now };
            await _dbSemaphore.WaitAsync();
            try { using (var db = new AppDbContext()) { db.Plants.Add(plant); await db.SaveChangesAsync(); } Plants.Add(plant); }
            finally { _dbSemaphore.Release(); }
            NewPlantName = "";
        }
        catch { }
    }

    private async void WaterPlant(object? p)
    {
        if (p is not Plant plant) return;
        plant.LastWatered = DateTime.Now;
        await _dbSemaphore.WaitAsync();
        try { using (var db = new AppDbContext()) { db.Plants.Update(plant); await db.SaveChangesAsync(); } PlantsView.Refresh(); }
        finally { _dbSemaphore.Release(); }
    }

    private async void DeletePlant(object? p)
    {
        if (p is not Plant plant) return;
        Plants.Remove(plant);
        await _dbSemaphore.WaitAsync();
        try { using (var db = new AppDbContext()) { db.Plants.Remove(plant); await db.SaveChangesAsync(); } }
        finally { _dbSemaphore.Release(); }
    }

    private async void DiagnosePlant(object? p)
    {
        if (SelectedPlantForDetails?.LatestImagePath == null || IsDiagnosing) return;
        IsDiagnosing = true;
        var res = await _apiService.DiagnosePlantAsync(SelectedPlantForDetails.LatestImagePath);
        if (res != null)
        {
            SelectedPlantForDetails.LastDiagnosis = res;
            SelectedPlantForDetails.LastDiagnosisDate = DateTime.Now;
            await _dbSemaphore.WaitAsync();
            try { using (var db = new AppDbContext()) { db.Plants.Update(SelectedPlantForDetails); await db.SaveChangesAsync(); } }
            finally { _dbSemaphore.Release(); }
            OnPropertyChanged(nameof(SelectedPlantForDetails));
        }
        IsDiagnosing = false;
    }

    private void OpenEditDialog(object? p) { if (p is Plant plant) { SelectedPlantForEdit = plant; EditPlantName = plant.Name; IsEditDialogOpen = true; OnPropertyChanged(nameof(IsEditDialogOpen)); } }
    private async void SaveEdit(object? p) { if (SelectedPlantForEdit != null && !string.IsNullOrWhiteSpace(EditPlantName)) { SelectedPlantForEdit.Name = EditPlantName; await _dbSemaphore.WaitAsync(); try { using (var db = new AppDbContext()) { db.Plants.Update(SelectedPlantForEdit); await db.SaveChangesAsync(); } } finally { _dbSemaphore.Release(); } PlantsView.Refresh(); IsEditDialogOpen = false; OnPropertyChanged(nameof(IsEditDialogOpen)); } }
    private void CancelEdit(object? p) { IsEditDialogOpen = false; OnPropertyChanged(nameof(IsEditDialogOpen)); }

    private async void UploadImage(object? p)
    {
        if (p is not Plant target) return;
        var dialog = new OpenFileDialog { Filter = "Images|*.jpg;*.png;*.jpeg" };
        if (dialog.ShowDialog() == true)
        {
            IsSearching = true;
            string dest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", $"{Guid.NewGuid()}{Path.GetExtension(dialog.FileName)}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(dialog.FileName, dest);
            var img = new PlantImage { ImagePath = dest, UploadDate = DateTime.Now, PlantId = target.Id };
            target.Images.Add(img);
            if (string.IsNullOrEmpty(target.CareGuide))
            {
                var ai = await _apiService.AnalyzePlantImageAsync(dest);
                if (ai != null) { target.ScientificName = ai.Value.ScientificName; target.CareGuide = ai.Value.CareGuide; target.WateringIntervalInDays = ai.Value.WateringIntervalDays; }
            }
            await _dbSemaphore.WaitAsync();
            try { using (var db = new AppDbContext()) { db.Plants.Update(target); await db.SaveChangesAsync(); } }
            finally { _dbSemaphore.Release(); }
            IsSearching = false;
            PlantsView.Refresh();
        }
    }
}