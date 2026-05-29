package com.claude4net.app

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.ViewModel

data class JobRun(val id: String, val name: String)
data class Message(val id: String, val sender: String, val text: String, val timestamp: Long = System.currentTimeMillis())

class ChatViewModel : ViewModel() {
    var jobs = mutableStateListOf<JobRun>()
        private set

    var messages = mutableStateMapOf<String, List<Message>>()
        private set

    var selectedJobId by mutableStateOf<String?>(null)
        private set

    var drawerOpen by mutableStateOf(false)
        private set

    var inputMessage by mutableStateOf("")

    init {
        // Add some mock data
        val job1 = JobRun("job_1", "Code Refactoring Agent")
        val job2 = JobRun("job_2", "Database Migrator")
        jobs.add(job1)
        jobs.add(job2)

        messages["job_1"] = listOf(
            Message("m1", "Agent", "Hello, I am ready to help refactor your code."),
            Message("m2", "User", "Please optimize the MainActivity layout.")
        )
        messages["job_2"] = listOf(
            Message("m3", "Agent", "Database Migration agent started."),
            Message("m4", "User", "Run baseline schema checks.")
        )

        selectedJobId = "job_1"
    }

    fun openDrawer() {
        drawerOpen = true
    }

    fun closeDrawer() {
        drawerOpen = false
    }

    fun selectJob(jobId: String) {
        selectedJobId = jobId
        closeDrawer()
    }

    fun createNewChat() {
        val newId = "job_${System.currentTimeMillis()}"
        val newJob = JobRun(newId, "New Agent Run #${jobs.size + 1}")
        jobs.add(newJob)
        messages[newId] = listOf(
            Message("init_${System.currentTimeMillis()}", "Agent", "Welcome to a new agent session!")
        )
        selectedJobId = newId
        closeDrawer()
    }

    fun setInput(text: String) {
        inputMessage = text
    }

    fun sendMessage() {
        val text = inputMessage.trim()
        if (text.isEmpty()) return
        val jobId = selectedJobId ?: return

        val userMsg = Message("msg_u_${System.currentTimeMillis()}", "User", text)
        val currentMsgs = messages[jobId] ?: emptyList()
        messages[jobId] = currentMsgs + userMsg
        inputMessage = ""

        // Generate a mock response
        val agentMsg = Message("msg_a_${System.currentTimeMillis()}", "Agent", "Mock agent response to: \"$text\"")
        messages[jobId] = (messages[jobId] ?: emptyList()) + agentMsg
    }
}
