# Firebase Auth & Firestore Production Security Rules Specification

```javascript
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    
    // Helper Functions
    function isAuthenticated() {
      return request.auth != null;
    }
    
    function isOwner(userId) {
      return request.auth.uid == userId;
    }
    
    function isAdmin() {
      return request.auth.token.email in ['admin@apextrader.ai', 'owner@apextrader.ai'];
    }

    // 1. User Profiles Collection
    match /users/{userId} {
      allow read: if isAuthenticated() && (isOwner(userId) || isAdmin());
      allow create: if isAuthenticated() && isOwner(userId);
      allow update: if isAuthenticated() && (isOwner(userId) || isAdmin());
      allow delete: if isAdmin();
    }

    // 2. Licenses Collection (Gated Access & Remote Kill-Switch)
    match /licenses/{licenseId} {
      allow read: if isAuthenticated() && (resource.data.userId == request.auth.uid || isAdmin());
      allow write: if isAdmin(); // Only Admin / Stripe Webhook can write licenses
    }

    // 3. HWID Registrations Collection
    match /hwid_registrations/{hwidId} {
      allow read: if isAuthenticated() && (resource.data.userId == request.auth.uid || isAdmin());
      allow create: if isAuthenticated(); // Auto-bind on first bot launch
      allow update, delete: if isAdmin();
    }
  }
}
```
