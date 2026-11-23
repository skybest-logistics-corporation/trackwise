package com.trackwise.driver.model

import com.trackwise.driver.api.models.TripDto
import com.trackwise.driver.api.models.TripRescueDto

/**
 * The trip as this driver runs it. A trip's own driver gets it as it is, unless they moved to a rescue
 * truck themselves (see [movedToRescueTruck]). Another rescue crew gets only the
 * drops they took over, on their truck: the run starts at the transfer and ends once the last of those
 * drops is left, since the office enters the rescue truck's garage times. Everything else on Today
 * then works on it unchanged, and taps still go to the real trip and stops.
 */
fun TripDto.forDriver(driverId: String): TripDto {
    val rescue = rescues.orEmpty().lastOrNull { it.driverId == driverId } ?: return this
    if (this.driverId == driverId) return movedToRescueTruck(rescue)

    val taken = rescue.stopIds.orEmpty().toSet()
    val drops = stops.orEmpty().filter { it.id in taken }.map { it.copy(rescueId = null) }
    val lastLeft = drops
        .takeIf { it.isNotEmpty() && it.all { s -> s.departedAt != null } }
        ?.maxOf { it.departedAt!! }

    return copy(
        stops = drops,
        loads = loads.orEmpty().filter { load -> drops.any { it.loadId == load.id } },
        truckId = rescue.truckId,
        truckNumber = rescue.truckNumber,
        driverId = rescue.driverId,
        driverName = rescue.driverName,
        helperId = rescue.helperId,
        helperName = rescue.helperName,
        secondHelperId = rescue.secondHelperId,
        secondHelperName = rescue.secondHelperName,
        garageDepartedAt = rescue.transferredAt,
        garageDepartureRecordedAt = null,
        garageDepartureSource = null,
        garageArrivedAt = lastLeft,
        garageArrivalRecordedAt = null,
        garageArrivalSource = null
    )
}

/**
 * The trip's own crew moved to the rescue truck - Cold Chain's usual "change unit" when the office
 * just brings them another truck. The drops it took over stay theirs, and Today follows the new truck,
 * so its location and truck checks go to the truck they are in now. Garage times stay the trip's.
 */
private fun TripDto.movedToRescueTruck(rescue: TripRescueDto): TripDto {
    val taken = rescue.stopIds.orEmpty().toSet()
    return copy(
        stops = stops.orEmpty().map { if (it.id in taken) it.copy(rescueId = null) else it },
        truckId = rescue.truckId,
        truckNumber = rescue.truckNumber
    )
}
