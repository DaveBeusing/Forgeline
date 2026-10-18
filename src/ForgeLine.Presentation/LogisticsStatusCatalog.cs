using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class LogisticsStatusCatalog
{
    public static string Service(PlayerWorkSummary work) => work.VehicleQueue is { } queue ? queue.Status switch
    {
        VehicleDockStatus.Aligning => "ALIGNING",
        VehicleDockStatus.Servicing => queue.Service == VehicleQueueService.CargoDelivery ? "UNLOADING" : "LOADING",
        VehicleDockStatus.Exiting => "LEAVING BAY",
        VehicleDockStatus.Docked => "DOCKED",
        VehicleDockStatus.Reserved or VehicleDockStatus.Approaching => "APPROACHING BAY",
        _ => queue.WaitReason switch
        {
            VehicleQueueWaitReason.Full => "WAITING AREA FULL",
            VehicleQueueWaitReason.GeometryBlocked => "ACCESS BLOCKED",
            VehicleQueueWaitReason.Unavailable => "NO DRIVE FUEL",
            VehicleQueueWaitReason.OccupiedExit => "WAITING FOR EXIT",
            _ => "WAITING FOR LOADING BAY"
        }
    } : work.LoadingQueue is { } loading ? loading.ExitBlocked ? "EXIT BLOCKED" :
        loading.ServiceOccupied ? "BAY OCCUPIED" : "VEHICLES QUEUED" : "ACCESS NOT OBSERVED";

    public static string Advice(LogisticsTransportRequestFailureReason failure) => failure switch
    {
        LogisticsTransportRequestFailureReason.NoSourceSurplus => "CHECK SOURCE STOCK, PRODUCTION AND RESERVED AMOUNTS",
        LogisticsTransportRequestFailureReason.NoTruckAvailable => "CHECK CARGO WORK, RESERVATIONS AND DRIVE FUEL; SUPPLY TRUCKS ARE SEPARATE",
        LogisticsTransportRequestFailureReason.NoRoute => "CHECK INFRASTRUCTURE; GRAPH CONNECTION AND LOCAL ACCESS ARE SEPARATE",
        LogisticsTransportRequestFailureReason.CapacitySaturated => "CHECK LINK OR HUB LOAD; A BUSY BAY IS A SEPARATE WAIT",
        LogisticsTransportRequestFailureReason.DestinationFull => "CONSUME OR MOVE DESTINATION STOCK",
        _ => "OPERATIONS / BLOCKED / OPEN EXISTING CONTROLS"
    };

    public static string DriveTank(PlayerSupplyActionReadModel supply) => supply.DriveFuelCapacity > 0
        ? $"DRIVE TANK {supply.DriveFuelQuantity:0.##}/{supply.DriveFuelCapacity:0.##}" : "NO DRIVE TANK";

    public static string PolicyDetails(PlayerLogisticsActionReadModel logistics, PlayerStockPolicyActionReadModel stock) =>
        $"LOCAL {stock.CurrentQuantity:0.##} / FREE {stock.AvailableQuantity:0.##} / RESERVED {stock.ReservedQuantity:0.##}; " +
        $"REQUESTED {stock.RequestedQuantity:0.##} / ASSIGNED INCOMING {stock.AnnouncedQuantity:0.##}; " +
        $"SHARED STORAGE {logistics.InventoryQuantity:0.##}/{logistics.InventoryCapacity:0.##}";

    public static string ServiceDetails(PlayerWorkSummary work) => work.VehicleQueue is { } queue ?
        $"{Service(work)} / POSITION {(queue.Position > 0 ? queue.Position.ToString(System.Globalization.CultureInfo.InvariantCulture) : "N/A")} / LOADING BAY / {work.BlockReason}" :
        work.LoadingQueue is { } loading ? $"{Service(work)} / WAITING {loading.WaitingCount}" : Service(work);
}
