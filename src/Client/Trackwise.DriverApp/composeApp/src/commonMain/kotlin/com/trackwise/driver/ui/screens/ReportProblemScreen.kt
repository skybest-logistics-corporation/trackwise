package com.trackwise.driver.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.Send
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.trackwise.driver.ui.components.CardContainer
import com.trackwise.driver.ui.components.capture.CaptureScreenScaffold
import com.trackwise.driver.ui.components.capture.PhotoCaptureSection
import com.trackwise.driver.ui.components.capture.rememberCameraCapture
import com.trackwise.driver.ui.components.capture.rememberGalleryPick
import com.trackwise.driver.ui.components.today.BigActionButton
import com.trackwise.driver.ui.components.today.BigOutlinedButton
import com.trackwise.driver.ui.i18n.LocalStrings
import com.trackwise.driver.ui.i18n.Strings
import com.trackwise.driver.ui.theme.StepDone
import com.trackwise.driver.ui.theme.StepGo
import com.trackwise.driver.ui.theme.StepProblem
import com.trackwise.driver.viewmodel.ProblemKind
import com.trackwise.driver.viewmodel.ReportProblemUiState
import com.trackwise.driver.viewmodel.ReportProblemViewModel

/**
 * The crew's "Problem" button: three quick questions, photos if they can, and the case goes to the
 * office. Once it's in, the screen keeps the call button and photo sending in reach.
 */
@Composable
fun ReportProblemScreen(
    onNavigateBack: () -> Unit,
    onOpenUrl: (String) -> Unit,
    viewModel: ReportProblemViewModel
) {
    val s = LocalStrings.current
    val state by viewModel.uiState.collectAsState()
    val onCapturePhoto = rememberCameraCapture(onPhotoCaptured = viewModel::addPhoto)
    val onPickPhoto = rememberGalleryPick(onPhotoCaptured = viewModel::addPhoto)
    val call = state.officePhone?.let { phone -> { onOpenUrl("tel:${phone.filter { it.isDigit() || it == '+' }}") } }

    CaptureScreenScaffold(
        title = s.problemTitle,
        error = null,
        isSuccess = false,
        onClearError = {},
        onNavigateBack = onNavigateBack
    ) {
        if (state.incidentId != null) {
            item { SentCard(state, s) }
        } else {
            item {
                Question(s.problemWhat) {
                    Choice(s.kindAccident, state.kind == ProblemKind.ACCIDENT) { viewModel.setKind(ProblemKind.ACCIDENT) }
                    Choice(s.kindBreakdown, state.kind == ProblemKind.BREAKDOWN) { viewModel.setKind(ProblemKind.BREAKDOWN) }
                    Choice(s.kindOther, state.kind == ProblemKind.OTHER) { viewModel.setKind(ProblemKind.OTHER) }
                }
            }
            item { YesNo(s.truckCanGoOn, state.truckCanGoOn, viewModel::setTruckCanGoOn) }
            item { YesNo(s.anyoneHurt, state.anyoneHurt, viewModel::setAnyoneHurt) }
            item {
                OutlinedTextField(
                    value = state.note,
                    onValueChange = viewModel::setNote,
                    label = { Text(s.problemNote) },
                    modifier = Modifier.fillMaxWidth(),
                    minLines = 2
                )
            }
        }

        item {
            PhotoCaptureSection(
                photos = state.photos,
                onAddPhoto = onCapturePhoto,
                onPickPhoto = onPickPhoto,
                onRemovePhoto = viewModel::removePhoto,
                title = s.problemPhotos
            )
        }

        state.error?.let { error ->
            item {
                Text(
                    text = error.ifBlank { if (state.incidentId == null) s.problemNotSent else s.photosNotSent },
                    color = StepProblem,
                    fontWeight = FontWeight.Bold
                )
            }
        }

        item {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                when {
                    state.incidentId == null -> BigActionButton(
                        text = s.sendProblem,
                        icon = Icons.Default.Send,
                        color = StepProblem,
                        onClick = viewModel::send,
                        enabled = state.canSend
                    )
                    state.photosWaiting > 0 -> BigActionButton(
                        text = s.sendPhotos,
                        icon = Icons.Default.Send,
                        color = StepGo,
                        onClick = viewModel::send,
                        enabled = !state.sending
                    )
                }
                if (call != null) {
                    BigActionButton(text = s.callOffice, icon = Icons.Default.Phone, color = StepGo, onClick = call)
                }
                if (state.incidentId != null) {
                    BigOutlinedButton(text = s.finish, icon = Icons.Default.Check, onClick = onNavigateBack)
                }
            }
        }
    }
}

@Composable
private fun SentCard(state: ReportProblemUiState, s: Strings) {
    CardContainer {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Default.CheckCircle, contentDescription = null, tint = StepDone)
                Text(
                    s.problemSentTitle(state.caseNumber.orEmpty()),
                    style = MaterialTheme.typography.titleLarge,
                    fontWeight = FontWeight.Bold,
                    color = StepDone
                )
            }
            Text(s.problemSentBody)
        }
    }
}

@Composable
private fun Question(title: String, choices: @Composable () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) { choices() }
    }
}

@Composable
private fun YesNo(title: String, value: Boolean?, onChange: (Boolean) -> Unit) {
    val s = LocalStrings.current
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Choice(s.yes, value == true, Modifier.weight(1f)) { onChange(true) }
            Choice(s.no, value == false, Modifier.weight(1f)) { onChange(false) }
        }
    }
}

/** Big enough to hit with a shaking hand. */
@Composable
private fun Choice(label: String, selected: Boolean, modifier: Modifier = Modifier.fillMaxWidth(), onClick: () -> Unit) {
    FilterChip(
        selected = selected,
        onClick = onClick,
        label = { Text(label, fontSize = 17.sp, fontWeight = FontWeight.Bold) },
        leadingIcon = if (selected) {
            { Icon(Icons.Default.Check, contentDescription = null) }
        } else {
            null
        },
        colors = FilterChipDefaults.filterChipColors(
            selectedContainerColor = StepGo,
            selectedLabelColor = MaterialTheme.colorScheme.onPrimary,
            selectedLeadingIconColor = MaterialTheme.colorScheme.onPrimary
        ),
        modifier = modifier.heightIn(min = 52.dp)
    )
}
