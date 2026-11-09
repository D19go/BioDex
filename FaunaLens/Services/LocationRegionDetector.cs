using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;

namespace FaunaLens.Services;

// Descobre o estado brasileiro onde o celular está, com localização aproximada (rede) e o geocodificador
// do Android (que usa internet). Coordenadas não são gravadas nem registradas.
public static class LocationRegionDetector
{
    public const string CoarseLocationPermission = "android.permission.ACCESS_COARSE_LOCATION";

    static readonly TimeSpan FixTimeout = TimeSpan.FromSeconds(20);
    static readonly TimeSpan MaxLastKnownAge = TimeSpan.FromHours(2);

    public static bool HasPermission(Context context) =>
        context.CheckSelfPermission(CoarseLocationPermission) == Permission.Granted;

    // Devolve o estado canônico (ex.: "São Paulo") ou null se não foi possível descobrir.
    public static async Task<string?> DetectStateAsync(Context context, CancellationToken ct)
    {
        if (!HasPermission(context) || !Geocoder.IsPresent)
            return null;

        var location = await GetLocationAsync(context, ct);
        if (location is null)
            return null;

        var adminArea = await Task.Run(() => ReadAdminArea(context, location.Latitude, location.Longitude), ct);
        return BrazilRegions.Canonical(adminArea);
    }

    static async Task<Location?> GetLocationAsync(Context context, CancellationToken ct)
    {
        if (context.GetSystemService(Context.LocationService) is not LocationManager manager)
            return null;

        var fallback = LastKnown(manager);
        if (!manager.IsProviderEnabled(LocationManager.NetworkProvider))
            return fallback;

        var fix = new TaskCompletionSource<Location?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new OneShotListener(location => fix.TrySetResult(location));
        manager.RequestLocationUpdates(LocationManager.NetworkProvider, 0L, 0f, listener, Looper.MainLooper!);

        try
        {
            var finished = await Task.WhenAny(fix.Task, Task.Delay(FixTimeout, ct));
            return finished == fix.Task ? fix.Task.Result : fallback;
        }
        finally
        {
            manager.RemoveUpdates(listener);
        }
    }

    static Location? LastKnown(LocationManager manager)
    {
        Location? best = null;
        foreach (var provider in new[] { LocationManager.NetworkProvider, LocationManager.PassiveProvider })
        {
            Location? candidate;
            try
            {
                candidate = manager.GetLastKnownLocation(provider);
            }
            catch (Exception)
            {
                continue;
            }

            if (candidate is not null && (best is null || candidate.Time > best.Time))
                best = candidate;
        }

        if (best is null)
            return null;

        var ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - best.Time;
        return ageMs <= MaxLastKnownAge.TotalMilliseconds ? best : null;
    }

    static string? ReadAdminArea(Context context, double latitude, double longitude)
    {
        try
        {
            // pt-BR para o geocodificador devolver os nomes dos estados em português.
            var geocoder = new Geocoder(context, new Java.Util.Locale("pt", "BR"));
            var addresses = geocoder.GetFromLocation(latitude, longitude, 1);
            return addresses?.FirstOrDefault()?.AdminArea;
        }
        catch (Exception)
        {
            return null;
        }
    }

    sealed class OneShotListener : Java.Lang.Object, ILocationListener
    {
        readonly Action<Location> onLocation;

        public OneShotListener(Action<Location> onLocation) => this.onLocation = onLocation;

        public void OnLocationChanged(Location location) => onLocation(location);

        public void OnProviderDisabled(string provider) { }

        public void OnProviderEnabled(string provider) { }
    }
}
