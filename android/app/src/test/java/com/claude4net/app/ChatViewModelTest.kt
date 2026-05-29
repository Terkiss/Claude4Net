package com.claude4net.app

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ChatViewModelTest {

    @Test
    fun testInitialState() {
        val viewModel = ChatViewModel()
        assertFalse(viewModel.drawerOpen)
        assertEquals("job_1", viewModel.selectedJobId)
        assertEquals(2, viewModel.jobs.size)
        assertEquals(2, viewModel.messages["job_1"]?.size)
        assertEquals("", viewModel.inputMessage)
    }

    @Test
    fun testDrawerOpenClose() {
        val viewModel = ChatViewModel()
        assertFalse(viewModel.drawerOpen)

        viewModel.openDrawer()
        assertTrue(viewModel.drawerOpen)

        viewModel.closeDrawer()
        assertFalse(viewModel.drawerOpen)
    }

    @Test
    fun testSelectJob() {
        val viewModel = ChatViewModel()
        viewModel.openDrawer()
        assertTrue(viewModel.drawerOpen)

        viewModel.selectJob("job_2")
        assertEquals("job_2", viewModel.selectedJobId)
        assertFalse(viewModel.drawerOpen)
    }

    @Test
    fun testCreateNewChat() {
        val viewModel = ChatViewModel()
        val initialJobsCount = viewModel.jobs.size

        viewModel.openDrawer()
        viewModel.createNewChat()

        assertEquals(initialJobsCount + 1, viewModel.jobs.size)
        val newJobId = viewModel.selectedJobId
        assertNotNull(newJobId)
        assertTrue(newJobId!!.startsWith("job_"))
        assertFalse(viewModel.drawerOpen)

        val messages = viewModel.messages[newJobId]
        assertNotNull(messages)
        assertEquals(1, messages?.size)
        assertEquals("Agent", messages?.first()?.sender)
        assertEquals("Welcome to a new agent session!", messages?.first()?.text)
    }

    @Test
    fun testSendMessage() {
        val viewModel = ChatViewModel()
        viewModel.setInput("Hello agent, execute task 104")
        assertEquals("Hello agent, execute task 104", viewModel.inputMessage)

        val initialMessagesSize = viewModel.messages[viewModel.selectedJobId]?.size ?: 0
        viewModel.sendMessage()

        // Input should be cleared
        assertEquals("", viewModel.inputMessage)

        val updatedMessages = viewModel.messages[viewModel.selectedJobId]
        assertNotNull(updatedMessages)
        // User message + Agent reply = 2 new messages
        assertEquals(initialMessagesSize + 2, updatedMessages?.size)

        val userMessage = updatedMessages?.get(updatedMessages.size - 2)
        assertEquals("User", userMessage?.sender)
        assertEquals("Hello agent, execute task 104", userMessage?.text)

        val agentMessage = updatedMessages?.last()
        assertEquals("Agent", agentMessage?.sender)
        assertTrue(agentMessage?.text?.contains("Mock agent response") == true)
    }

    @Test
    fun testApproveJob() {
        val viewModel = ChatViewModel()
        // Message "m2" in job_1 has pending approval
        val currentMsgs = viewModel.messages["job_1"]
        val msgM2 = currentMsgs?.find { it.id == "m2" }
        assertNotNull(msgM2)
        assertTrue(msgM2?.hasPendingApproval == true)

        viewModel.approveJob("m2")

        val updatedMsgs = viewModel.messages["job_1"]
        val updatedM2 = updatedMsgs?.find { it.id == "m2" }
        assertNotNull(updatedM2)
        assertFalse(updatedM2?.hasPendingApproval == true)
        assertTrue(updatedM2?.text?.contains("[APPROVED BY USER]") == true)
    }

    @Test
    fun testRejectJob() {
        val viewModel = ChatViewModel()
        // Message "m2" in job_1 has pending approval
        val currentMsgs = viewModel.messages["job_1"]
        val msgM2 = currentMsgs?.find { it.id == "m2" }
        assertNotNull(msgM2)
        assertTrue(msgM2?.hasPendingApproval == true)

        viewModel.rejectJob("m2")

        val updatedMsgs = viewModel.messages["job_1"]
        val updatedM2 = updatedMsgs?.find { it.id == "m2" }
        assertNotNull(updatedM2)
        assertFalse(updatedM2?.hasPendingApproval == true)
        assertTrue(updatedM2?.text?.contains("[REJECTED BY USER]") == true)
    }
}
