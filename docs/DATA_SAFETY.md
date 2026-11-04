# Google Play "Data safety" form – suggested answers (to be confirmed by the owner)

Based on what the code actually does (checked in `scripts/autoload/*.gd` and `backend/`).

| Question | Answer | Reason |
|---|---|---|
| Does the app collect or share user data? | Yes, collects | Cloud save + purchases + optional parent email |
| Is data encrypted in transit? | Yes | HTTPS only to Supabase / Google |
| Can users request deletion? | Yes, in app | Settings → Parents → Delete my data (`hf_delete_my_data`) |
| Personal info → Email address | Collected, optional | Only if a parent links it; purpose: account management |
| App activity → In-app actions | Collected | App functionality, fraud prevention |
| Financial info → Purchase history | Collected | App functionality |
| Device or other IDs | Collected by AdMob SDK | Advertising (non-personalised) – declare per Google's AdMob guidance |
| Location, contacts, photos, messages, health | Not collected | – |
| Data shared with third parties | Declare AdMob as required by Google | – |

Target audience in Play Console: choose the children's age groups → the app must join the
**Families policy**: only Families-certified ad SDK settings, no personalised ads, privacy policy URL required.
