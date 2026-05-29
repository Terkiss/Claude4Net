package com.claude4net.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material.icons.filled.Send
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
                        MainAppLayout(
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

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainAppLayout(
    securePreferences: SecurePreferences,
    onLogout: () -> Unit,
    viewModel: ChatViewModel = remember { ChatViewModel() }
) {
    val drawerState = rememberDrawerState(
        initialValue = if (viewModel.drawerOpen) DrawerValue.Open else DrawerValue.Closed
    )
    
    // Sync viewModel state with Compose drawerState
    LaunchedEffect(viewModel.drawerOpen) {
        if (viewModel.drawerOpen && drawerState.isClosed) {
            drawerState.open()
        } else if (!viewModel.drawerOpen && drawerState.isOpen) {
            drawerState.close()
        }
    }
    LaunchedEffect(drawerState.currentValue) {
        if (drawerState.currentValue == DrawerValue.Open) {
            viewModel.openDrawer()
        } else {
            viewModel.closeDrawer()
        }
    }

    ModalNavigationDrawer(
        drawerState = drawerState,
        drawerContent = {
            ModalDrawerSheet(
                modifier = Modifier.width(300.dp)
            ) {
                // Drawer Header
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp)
                ) {
                    Text(
                        text = "Claude4Net Agent",
                        style = MaterialTheme.typography.titleLarge,
                        color = MaterialTheme.colorScheme.primary
                    )
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = "Instance: ${securePreferences.getAppInstanceId().take(8)}...",
                        style = MaterialTheme.typography.bodySmall
                    )
                }
                
                Divider()

                // New Chat Button
                NavigationDrawerItem(
                    icon = { Icon(Icons.Default.Add, contentDescription = "New Chat") },
                    label = { Text("New Chat") },
                    selected = false,
                    onClick = {
                        viewModel.createNewChat()
                    },
                    modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp)
                )

                Divider()

                // List of active/previous job runs
                Text(
                    text = "Active/Previous Runs",
                    style = MaterialTheme.typography.labelMedium,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)
                )

                LazyColumn(
                    modifier = Modifier.weight(1f)
                ) {
                    items(viewModel.jobs) { job ->
                        NavigationDrawerItem(
                            label = { Text(job.name) },
                            selected = viewModel.selectedJobId == job.id,
                            onClick = {
                                viewModel.selectJob(job.id)
                            },
                            modifier = Modifier.padding(horizontal = 12.dp, vertical = 4.dp)
                        )
                    }
                }

                // Logout button
                Divider()
                NavigationDrawerItem(
                    label = { Text("Logout & Disconnect") },
                    selected = false,
                    onClick = onLogout,
                    modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp)
                )
            }
        }
    ) {
        val selectedJobName = viewModel.jobs.find { it.id == viewModel.selectedJobId }?.name ?: "No Job Selected"
        
        Scaffold(
            topBar = {
                TopAppBar(
                    title = { Text(selectedJobName) },
                    navigationIcon = {
                        IconButton(onClick = { viewModel.openDrawer() }) {
                            Icon(Icons.Default.Menu, contentDescription = "Open Drawer")
                        }
                    }
                )
            }
        ) { paddingValues ->
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(paddingValues)
            ) {
                val currentMessages = viewModel.messages[viewModel.selectedJobId] ?: emptyList()
                
                // Conversation Feed
                LazyColumn(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp),
                    reverseLayout = false
                ) {
                    items(currentMessages) { message ->
                        val isUser = message.sender == "User"
                        val alignment = if (isUser) Alignment.End else Alignment.Start
                        val bgColor = if (isUser) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.secondaryContainer
                        val textColor = if (isUser) MaterialTheme.colorScheme.onPrimaryContainer else MaterialTheme.colorScheme.onSecondaryContainer

                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(vertical = 4.dp),
                            horizontalAlignment = alignment
                        ) {
                            Surface(
                                shape = MaterialTheme.shapes.medium,
                                color = bgColor,
                                modifier = Modifier.widthIn(max = 280.dp)
                            ) {
                                Column(modifier = Modifier.padding(12.dp)) {
                                    Text(
                                        text = message.sender,
                                        style = MaterialTheme.typography.labelSmall,
                                        color = textColor.copy(alpha = 0.7f)
                                    )
                                    Spacer(modifier = Modifier.height(4.dp))
                                    Text(
                                        text = message.text,
                                        style = MaterialTheme.typography.bodyMedium,
                                        color = textColor
                                    )
                                }
                            }
                        }
                    }
                }

                // Bottom Input Area
                Surface(
                    tonalElevation = 2.dp,
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .navigationBarsPadding()
                            .imePadding()
                            .padding(8.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        OutlinedTextField(
                            value = viewModel.inputMessage,
                            onValueChange = { viewModel.setInput(it) },
                            placeholder = { Text("Type a message to agent...") },
                            modifier = Modifier
                                .weight(1f)
                                .padding(end = 8.dp),
                            maxLines = 4
                        )
                        IconButton(
                            onClick = { viewModel.sendMessage() },
                            enabled = viewModel.inputMessage.isNotBlank()
                        ) {
                            Icon(
                                imageVector = Icons.Default.Send,
                                contentDescription = "Send Message",
                                tint = if (viewModel.inputMessage.isNotBlank()) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outline
                            )
                        }
                    }
                }
            }
        }
    }
}
