package com.claude4net.app

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AuthScreen(
    securePreferences: SecurePreferences,
    onAuthSuccess: () -> Unit
) {
    var serverUrl by remember { mutableStateOf(securePreferences.getServerUrl().ifEmpty { "http://10.0.2.2:5277" }) }
    var pairingCode by remember { mutableStateOf("") }
    var statusMessage by remember { mutableStateOf("") }
    var isLoading by remember { mutableStateOf(false) }
    var pairingId by remember { mutableStateOf("") }
    var showPairingInput by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text("Claude4Net Android Client", fontSize = 24.sp, modifier = Modifier.padding(bottom = 32.dp))

        OutlinedTextField(
            value = serverUrl,
            onValueChange = { serverUrl = it },
            label = { Text("Server URL") },
            modifier = Modifier.fillMaxWidth()
        )

        Spacer(modifier = Modifier.height(16.dp))

        if (!showPairingInput) {
            Button(
                onClick = {
                    scope.launch {
                        isLoading = true
                        statusMessage = "Attempting LAN authentication..."
                        securePreferences.saveServerUrl(serverUrl)
                        val success = performLanAuth(serverUrl, securePreferences)
                        isLoading = false
                        if (success) {
                            statusMessage = "LAN Auth Successful!"
                            onAuthSuccess()
                        } else {
                            statusMessage = "LAN Auth failed or declined. Try Pairing Code."
                        }
                    }
                },
                enabled = !isLoading,
                modifier = Modifier.fillMaxWidth()
            ) {
                Text("Connect via LAN Approval")
            }

            Spacer(modifier = Modifier.height(8.dp))

            Button(
                onClick = {
                    scope.launch {
                        isLoading = true
                        statusMessage = "Requesting Pairing Code..."
                        securePreferences.saveServerUrl(serverUrl)
                        val pId = requestPairingCode(serverUrl, securePreferences)
                        isLoading = false
                        if (pId != null) {
                            pairingId = pId
                            showPairingInput = true
                            statusMessage = "Enter the 10-digit code shown on the server terminal."
                        } else {
                            statusMessage = "Failed to request pairing code."
                        }
                    }
                },
                enabled = !isLoading,
                modifier = Modifier.fillMaxWidth()
            ) {
                Text("Request 10-Digit Pairing Code")
            }
        } else {
            OutlinedTextField(
                value = pairingCode,
                onValueChange = { pairingCode = it },
                label = { Text("10-Digit Pairing Code") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.fillMaxWidth()
            )

            Spacer(modifier = Modifier.height(16.dp))

            Button(
                onClick = {
                    scope.launch {
                        isLoading = true
                        statusMessage = "Confirming pairing code..."
                        val success = confirmPairingCode(serverUrl, pairingId, pairingCode, securePreferences)
                        isLoading = false
                        if (success) {
                            statusMessage = "Pairing Successful!"
                            onAuthSuccess()
                        } else {
                            statusMessage = "Invalid or expired pairing code."
                        }
                    }
                },
                enabled = !isLoading,
                modifier = Modifier.fillMaxWidth()
            ) {
                Text("Submit Code")
            }

            Spacer(modifier = Modifier.height(8.dp))

            TextButton(
                onClick = {
                    showPairingInput = false
                    statusMessage = ""
                }
            ) {
                Text("Cancel")
            }
        }

        Spacer(modifier = Modifier.height(24.dp))
        
        if (statusMessage.isNotEmpty()) {
            Text(statusMessage, color = MaterialTheme.colorScheme.primary, fontSize = 14.sp)
        }
    }
}

private suspend fun performLanAuth(serverUrl: String, securePreferences: SecurePreferences): Boolean {
    return withContext(Dispatchers.IO) {
        try {
            val client = OkHttpClient.Builder().build()
            val json = JSONObject().apply {
                put("deviceName", android.os.Build.MODEL)
                put("appInstanceId", securePreferences.getAppInstanceId())
            }
            val body = json.toString().toRequestBody("application/json".toMediaType())
            val request = Request.Builder()
                .url("$serverUrl/api/auth/lan")
                .post(body)
                .build()

            client.newCall(request).execute().use { response ->
                if (response.isSuccessful) {
                    val respBody = response.body?.string() ?: ""
                    val respJson = JSONObject(respBody)
                    val token = respJson.getString("accessToken")
                    securePreferences.saveAccessToken(token)
                    true
                } else {
                    false
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
            false
        }
    }
}

private suspend fun requestPairingCode(serverUrl: String, securePreferences: SecurePreferences): String? {
    return withContext(Dispatchers.IO) {
        try {
            val client = OkHttpClient.Builder().build()
            val json = JSONObject().apply {
                put("deviceName", android.os.Build.MODEL)
                put("appInstanceId", securePreferences.getAppInstanceId())
            }
            val body = json.toString().toRequestBody("application/json".toMediaType())
            val request = Request.Builder()
                .url("$serverUrl/api/pairing/request")
                .post(body)
                .build()

            client.newCall(request).execute().use { response ->
                if (response.isSuccessful) {
                    val respBody = response.body?.string() ?: ""
                    val respJson = JSONObject(respBody)
                    respJson.optString("pairingId", "")
                } else {
                    null
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
            null
        }
    }
}

private suspend fun confirmPairingCode(
    serverUrl: String,
    pairingId: String,
    code: String,
    securePreferences: SecurePreferences
): Boolean {
    return withContext(Dispatchers.IO) {
        try {
            val client = OkHttpClient.Builder().build()
            val json = JSONObject().apply {
                put("pairingId", pairingId)
                put("code", code)
            }
            val body = json.toString().toRequestBody("application/json".toMediaType())
            val request = Request.Builder()
                .url("$serverUrl/api/pairing/confirm")
                .post(body)
                .build()

            client.newCall(request).execute().use { response ->
                if (response.isSuccessful) {
                    val respBody = response.body?.string() ?: ""
                    val respJson = JSONObject(respBody)
                    val token = respJson.getString("accessToken")
                    securePreferences.saveAccessToken(token)
                    true
                } else {
                    false
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
            false
        }
    }
}
