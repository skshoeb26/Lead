# Free hosting: Oracle Cloud Always Free + DuckDNS (₹0)

Everything below can be done from a phone browser. The server commands run in **Oracle Cloud Shell** (a terminal inside the Oracle website), so you don't need a laptop.

## Part A — Oracle account (10 min)
1. Open https://signup.cloud.oracle.com → choose **Home Region: India West (Mumbai)** or **India South (Hyderabad)**. ⚠️ The home region cannot be changed later.
2. Verify email and phone, add a Visa/Mastercard for identity verification (a small temporary charge is refunded). Stay on the **Free Tier / Always Free** account type.
3. Wait for the "account ready" email (minutes to a few hours).

## Part B — Create the server (10 min)
1. Console → ☰ Menu → **Compute → Instances → Create instance**.
2. Name: `leadpilot`.
3. **Image**: click *Edit* → *Change image* → **Canonical Ubuntu 24.04**.
4. **Shape**: *Change shape* → **Ampere** → `VM.Standard.A1.Flex` → 2 OCPU, 12 GB memory (Always Free allows up to 4 OCPU / 24 GB).
   - If you see "Out of capacity", try again later, try another availability domain, or pick `VM.Standard.E2.1.Micro` (AMD, 1 GB — works, slower).
5. **Networking**: keep "Create new virtual cloud network" and **Assign a public IPv4 address = Yes**.
6. **SSH keys**: choose **Generate a key pair for me** → *Save private key* (keep this file safe).
7. **Create**. When it's *Running*, copy the **Public IP address** (e.g. `140.238.x.x`).

## Part C — Open ports 80 and 443 in Oracle
1. On the instance page → **Subnet** link → **Security Lists** → *Default Security List*.
2. **Add Ingress Rules** (twice):
   - Source CIDR `0.0.0.0/0`, IP protocol TCP, Destination port `80`
   - Source CIDR `0.0.0.0/0`, IP protocol TCP, Destination port `443`

## Part D — Free web address (DuckDNS, 2 min)
1. Open https://www.duckdns.org → sign in with Google.
2. Type a name (e.g. `leadpilot-shoeb`) → **add domain**.
3. In the **current ip** box paste your Oracle public IP → **update ip**.
   Your server address is now `leadpilot-shoeb.duckdns.org`.

## Part E — Install LeadPilot with one command (15 min)
1. In the Oracle Console, tap the **Cloud Shell** icon (`>_`, top right).
2. Upload your private key: Cloud Shell menu (⚙ / ☰) → **Upload** → select the key file from Part B.
3. Run (replace the IP):
```bash
chmod 600 ~/ssh-key-*.key
ssh -i ~/ssh-key-*.key ubuntu@YOUR_PUBLIC_IP
```
   Type `yes` if asked.
4. Now on the server, run (replace domain and email):
```bash
curl -fsSL https://raw.githubusercontent.com/skshoeb26/Lead/ccr-c4811066-f5sst5/deploy/scripts/server-setup.sh -o setup.sh
bash setup.sh leadpilot-shoeb.duckdns.org you@gmail.com
```
   It installs everything, creates secure passwords, gets a free HTTPS certificate and starts LeadPilot. At the end it prints **"LeadPilot AI is live at https://…"**.

## Part F — Connect the app
Open LeadPilot AI on your phone → **Server** → `https://leadpilot-shoeb.duckdns.org` → **Save server** → **First time? Set up owner account**.

You can now use the app with demo-free real data once API keys are added (see `SETUP-CLAUDE.md`, `SETUP-GOOGLE-PLACES.md`, `SETUP-GMAIL.md`). Add keys with:
```bash
nano ~/Lead/deploy/.env          # paste keys, Ctrl+O Enter to save, Ctrl+X to exit
cd ~/Lead/deploy && sudo docker compose up -d
```

## Troubleshooting
- **"does not point to … yet"** → update the IP on duckdns.org, wait 1 minute, run `bash setup.sh …` again.
- **HTTPS certificate fails** → check Part C ingress rules for ports 80 and 443.
- **Logs** → `cd ~/Lead/deploy && sudo docker compose logs --tail 100 api worker`
- Oracle may reclaim Always Free instances that sit idle at very low usage for long periods; LeadPilot's background jobs keep it active, but keep backups (see DEPLOYMENT.md).
