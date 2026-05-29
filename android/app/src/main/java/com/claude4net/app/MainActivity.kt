package com.claude4net.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

class MainActivity : ComponentActivity() {
    private lateinit var securePreferences: SecurePreferences

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        securePreferences = SecurePreferences(this)

        setContent {
            MaterialTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    var isAuthenticated by remember { 
                        mutableStateOf(securePreferences.getAccessToken().isNotEmpty()) 
                    }

                    if (isAuthenticated) {
                        AuthenticatedMainScreen(
                            securePreferences = securePreferences,
                            onLogout = {
                                securePreferences.clearAuth()
                                isAuthenticated = false
                            }
                        )
                    } else {
                        AuthScreen(
                            securePreferences = securePreferences,
                            onAuthSuccess = {
                                isAuthenticated = true
                            }
                        )
                    }
                }
            }
        }
    }
}

@Composable
fun AuthenticatedMainScreen(
    securePreferences: SecurePreferences,
    onLogout: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text("Successfully Authenticated!", fontSize = 22.sp)
        Spacer(modifier = Modifier.height(16.dp))
        Text("App Instance ID: ${securePreferences.getAppInstanceId()}", fontSize = 12.sp)
        Text("Access Token: ${securePreferences.getAccessToken().take(12)}...", fontSize = 12.sp)
        Spacer(modifier = Modifier.height(32.dp))
        Button(onClick = onLogout) {
            Text("Logout & Disconnect")
        }
    }
}
