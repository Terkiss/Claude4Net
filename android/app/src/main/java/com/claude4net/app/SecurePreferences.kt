package com.claude4net.app

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import java.util.UUID

class SecurePreferences(context: Context) {
    private val sharedPreferences: SharedPreferences

    companion object {
        private const val PREFS_FILE_NAME = "secure_prefs"
        private const val KEY_ACCESS_TOKEN = "access_token"
        private const val KEY_APP_INSTANCE_ID = "app_instance_id"
        private const val KEY_SERVER_URL = "server_url"
    }

    init {
        val masterKeyAlias = MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC)
        sharedPreferences = EncryptedSharedPreferences.create(
            PREFS_FILE_NAME,
            masterKeyAlias,
            context,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
        
        // Initialize App Instance ID if not exists
        if (getAppInstanceId().isEmpty()) {
            val newId = UUID.randomUUID().toString()
            sharedPreferences.edit().putString(KEY_APP_INSTANCE_ID, newId).apply()
        }
    }

    fun saveAccessToken(token: String) {
        sharedPreferences.edit().putString(KEY_ACCESS_TOKEN, token).apply()
    }

    fun getAccessToken(): String {
        return sharedPreferences.getString(KEY_ACCESS_TOKEN, "") ?: ""
    }

    fun getAppInstanceId(): String {
        return sharedPreferences.getString(KEY_APP_INSTANCE_ID, "") ?: ""
    }

    fun saveServerUrl(url: String) {
        sharedPreferences.edit().putString(KEY_SERVER_URL, url).apply()
    }

    fun getServerUrl(): String {
        return sharedPreferences.getString(KEY_SERVER_URL, "") ?: ""
    }

    fun clearAuth() {
        sharedPreferences.edit().remove(KEY_ACCESS_TOKEN).apply()
    }
}
