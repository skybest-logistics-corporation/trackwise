package com.trackwise.driver.viewmodel

import com.trackwise.driver.api.ApiException
import com.trackwise.driver.api.DriverApi
import com.trackwise.driver.api.models.IncidentKind
import com.trackwise.driver.api.models.ReportDriverIncidentCommand
import com.trackwise.driver.api.throwIfFailed
import com.trackwise.driver.model.FileUploadData
import com.trackwise.driver.model.toFormParts
import com.trackwise.driver.service.LocationService
import com.trackwise.driver.service.PreferencesManager
import com.trackwise.driver.util.Logger
import com.trackwise.driver.viewmodel.base.BaseViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update

/** The kinds a crew picks from on the roadside; the office sorts out anything finer. */
enum class ProblemKind(val api: IncidentKind) {
    ACCIDENT(IncidentKind.ACCIDENT),
    BREAKDOWN(IncidentKind.BREAKDOWN),
    OTHER(IncidentKind.OTHER)
}

data class ReportProblemUiState(
    val kind: ProblemKind? = null,
    val truckCanGoOn: Boolean? = null,
    val anyoneHurt: Boolean? = null,
    val note: String = "",
    val photos: List<CapturedPhoto> = emptyList(),
    val latitude: Double? = null,
    val longitude: Double? = null,
    val sending: Boolean = false,
    /** Set once the office has the case; the screen then offers photos and the call. */
    val incidentId: String? = null,
    val caseNumber: String? = null,
    /** Photos already on the case, so a retry sends only the rest. */
    val photosSent: Int = 0,
    /** The API's refusal in its own words; blank when it never got there (no signal). */
    val error: String? = null,
    val officePhone: String? = null
) {
    val canSend: Boolean get() = !sending && kind != null && truckCanGoOn != null && anyoneHurt != null
    val photosWaiting: Int get() = photos.size - photosSent
}

class ReportProblemViewModel(
    private val driverApi: DriverApi,
    private val locationService: LocationService,
    private val preferencesManager: PreferencesManager,
    private val tripId: String
) : BaseViewModel() {

    private val _state = MutableStateFlow(ReportProblemUiState())
    val uiState: StateFlow<ReportProblemUiState> = _state.asStateFlow()

    init {
        launchSafely {
            _state.update { it.copy(officePhone = preferencesManager.getOfficePhone()) }
            locationService.getCurrentLocation()?.let { loc ->
                _state.update { it.copy(latitude = loc.latitude, longitude = loc.longitude) }
            }
        }
    }

    fun setKind(kind: ProblemKind) = _state.update { it.copy(kind = kind) }
    fun setTruckCanGoOn(value: Boolean) = _state.update { it.copy(truckCanGoOn = value) }
    fun setAnyoneHurt(value: Boolean) = _state.update { it.copy(anyoneHurt = value) }
    fun setNote(note: String) = _state.update { it.copy(note = note) }
    fun addPhoto(photo: CapturedPhoto) = _state.update { it.copy(photos = it.photos + photo) }

    fun removePhoto(photoId: String) = _state.update { state ->
        val index = state.photos.indexOfFirst { it.id == photoId }
        if (index < state.photosSent) state else state.copy(photos = state.photos.filter { it.id != photoId })
    }

    /** Opens the case, then sends whatever photos are ready; a second press only resends the photos. */
    fun send() {
        val state = _state.value
        if (state.sending) return
        if (state.incidentId == null && !state.canSend) return

        _state.update { it.copy(sending = true, error = null) }
        launchSafely(onError = { e ->
            Logger.e("Problem report failed", e)
            _state.update {
                it.copy(sending = false, error = (e as? ApiException)?.takeIf { x -> x.statusCode == 400 }?.message.orEmpty())
            }
        }) {
            val incidentId = state.incidentId ?: report(state)
            sendPhotos(incidentId)
            _state.update { it.copy(sending = false) }
        }
    }

    private suspend fun report(state: ReportProblemUiState): String {
        val incident = driverApi.reportDriverIncident(
            tripId,
            ReportDriverIncidentCommand(
                tripId = tripId,
                kind = state.kind!!.api,
                note = state.note.trim().ifBlank { null },
                vehicleDrivable = state.truckCanGoOn,
                anyInjuries = state.anyoneHurt,
                latitude = state.latitude,
                longitude = state.longitude
            )
        ).also { it.throwIfFailed() }.body()
        val id = incident.id ?: error("The office got no case number back")
        _state.update { it.copy(incidentId = id, caseNumber = incident.caseNumber) }
        return id
    }

    private suspend fun sendPhotos(incidentId: String) {
        val state = _state.value
        val waiting = state.photos.drop(state.photosSent)
        if (waiting.isEmpty()) return
        val parts = waiting.map { FileUploadData(it.bytes, it.fileName, it.contentType) }.toFormParts("Photos")
        driverApi.addDriverIncidentPhotos(incidentId, parts).throwIfFailed()
        _state.update { it.copy(photosSent = state.photosSent + waiting.size) }
    }
}
