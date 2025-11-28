import time
import re
import requests
import io
import numpy as np
import easyocr
from PIL import Image
from urllib.parse import urlparse
from flask import Flask, request, jsonify, render_template_string
from openai import OpenAI
from transformers import pipeline

# ==========================================
# CONFIGURATION
# ==========================================

YOUR_SITE_URL = "https://openrouter.ai/api/v1"
ENABLE_IMAGES = False  # Set to True to enable Image Scanning/OCR/Context

# --- SAFETY CONFIGURATION ---
TRUSTED_DOMAINS = [
    "i.imgur.com", "imgur.com", 
    #"media.discordapp.net", "cdn.discordapp.com", 
    #"upload.wikimedia.org", "pbs.twimg.com",
    #"steamuserimages-a.akamaihd.net"
]

PLACEHOLDER_IMAGE = "https://i.imgur.com/tRaI8JO.jpg" 

# Block specific words in images (OCR)
BAD_WORDS_BLOCKLIST = ["badword1", "slur1", "slur2"] 

# Strictness Levels (0.0 to 1.0)
THRESHOLD_NSFW = 0.15  # Block if > 15% confident it's hardcore
THRESHOLD_SEXY = 0.50  # Block if > 50% confident it's softcore (lingerie/bikini)

# ==========================================
# WEB UI TEMPLATE
# ==========================================

HTML_TEMPLATE = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>GMod AI Chaos Controller</title>
    <style>
        body { background-color: #121212; color: #00ff41; font-family: 'Courier New', monospace; padding: 20px; text-align: center; }
        h1 { text-shadow: 0 0 10px #00ff41; }
        .container { max-width: 600px; margin: 0 auto; background: #1e1e1e; padding: 20px; border-radius: 10px; border: 1px solid #333; }
        textarea { width: 100%; height: 100px; background: #000; color: #fff; border: 1px solid #444; padding: 10px; font-size: 16px; margin-bottom: 10px; }
        button { background: #00ff41; color: #000; border: none; padding: 10px 20px; font-size: 18px; cursor: pointer; font-weight: bold; width: 100%; }
        button:hover { background: #00cc33; }
        button:disabled { background: #555; cursor: not-allowed; }
        #status { margin-top: 20px; text-align: left; background: #000; padding: 10px; border: 1px solid #333; height: 200px; overflow-y: auto; font-size: 12px; }
        .log-entry { margin-bottom: 5px; border-bottom: 1px solid #222; padding-bottom: 2px; }
        .error { color: #ff3333; }
        .success { color: #00ff41; }
    </style>
</head>
<body>
    <div class="container">
        <h1>AI CHAOS CONTROL</h1>
        <p>Type a request to mess with the streamer.</p>
        <textarea id="promptInput" placeholder="Ex: Make everyone tiny..."></textarea>
        <button id="sendBtn" onclick="sendPrompt()">SEND CHAOS</button>
        
        <div id="status"></div>
    </div>

    <script>
        const input = document.getElementById('promptInput');
        const btn = document.getElementById('sendBtn');
        const statusDiv = document.getElementById('status');

        function log(msg, type='normal') {
            const el = document.createElement('div');
            el.className = 'log-entry ' + type;
            el.innerText = `[${new Date().toLocaleTimeString()}] ${msg}`;
            statusDiv.prepend(el);
        }

        async function sendPrompt() {
            const text = input.value.trim();
            if (!text) return;

            input.disabled = true;
            btn.disabled = true;
            btn.innerText = "GENERATING...";

            try {
                const response = await fetch('/trigger', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ prompt: text })
                });
                
                const data = await response.json();
                
                if (data.status === "queued") {
                    log(`SUCCESS: ${text}`, 'success');
                    log(`> AI Generated Code (queued)`, 'normal');
                } else if (data.status === "ignored") {
                    log(`BLOCKED: ${data.message}`, 'error');
                } else {
                    log(`ERROR: ${JSON.stringify(data)}`, 'error');
                }
            } catch (err) {
                log(`NETWORK ERROR: ${err.message}`, 'error');
            }

            input.value = "";
            input.disabled = false;
            btn.disabled = false;
            btn.innerText = "SEND CHAOS";
            input.focus();
        }
        
        // Allow Ctrl+Enter to submit
        input.addEventListener('keydown', function(e) {
            if (e.ctrlKey && e.key === 'Enter') {
                sendPrompt();
            }
        });
    </script>
</body>
</html>
"""

# ==========================================
# SETUP & MODEL LOADING
# ==========================================

nsfw_classifier = None
captioner = None
ocr_reader = None

if ENABLE_IMAGES:
    print("Loading Local AI Models... (This includes downloading them if first run)")
    try:
        # 1. Advanced NSFW Detector (AdamCodd)
        print(" - Loading Advanced NSFW Detector (AdamCodd)...")
        nsfw_classifier = pipeline("image-classification", model="AdamCodd/vit-base-nsfw-detector")
        
        # 2. Smarter Captioner (BLIP Large)
        print(" - Loading Large Captioner (BLIP)...")
        captioner = pipeline("image-to-text", model="Salesforce/blip-image-captioning-large")
        
        # 3. EasyOCR (No external exe needed)
        print(" - Loading EasyOCR...")
        ocr_reader = easyocr.Reader(['en'], verbose=False) 
        
        print("All Models Loaded Successfully!")

    except Exception as e:
        print(f"\033[91m[ERROR] Failed to load AI models: {e}\033[0m")
        print("Continuing with Image Scanning DISABLED.")
        ENABLE_IMAGES = False
else:
    print("Image Scanning is DISABLED in config. Skipping heavy model loading.")

# Load API Key
try:
    with open("openrouter_api_key.txt", "r") as f:
        OPENROUTER_API_KEY = f.read().strip()
except FileNotFoundError:
    print("\033[91m[ERROR] openrouter_api_key.txt not found!\033[0m")
    exit(1)

app = Flask(__name__)
client = OpenAI(
    base_url=YOUR_SITE_URL,
    api_key=OPENROUTER_API_KEY,
)

command_queue = []

# --- LOCAL SCANNING LOGIC ---
def sanitize_and_scan(user_prompt):
    """
    Returns a tuple: (cleaned_prompt, image_context_string, was_blocked)
    """
    # If images are disabled, simply return the prompt as is (or strip URLs if you prefer)
    if not ENABLE_IMAGES:
        return user_prompt, "", False

    url_pattern = r'http[s]?://(?:[a-zA-Z]|[0-9]|[$-_@.&+]|[!*\\(\\),]|(?:%[0-9a-fA-F][0-9a-fA-F]))+'
    urls = re.findall(url_pattern, user_prompt)
    
    clean_prompt = user_prompt
    extracted_context = []
    was_blocked = False

    for url in urls:
        parsed = urlparse(url)
        domain = parsed.netloc.lower()
        
        # 1. Basic Whitelist Check
        is_trusted = any(trusted in domain for trusted in TRUSTED_DOMAINS)
        if not is_trusted:
            print(f"[SAFETY] Blocked untrusted domain: {domain}")
            clean_prompt = clean_prompt.replace(url, PLACEHOLDER_IMAGE)
            was_blocked = True
            continue 

        # 2. Local AI Scan
        print(f"[SAFETY] Downloading image for local scan: {url}")
        try:
            # Download image to memory with browser spoofing
            headers = {'User-Agent': 'Mozilla/5.0'}
            response = requests.get(url, timeout=5, headers=headers)
            if response.status_code != 200:
                raise Exception(f"HTTP Error {response.status_code}")
            
            # Convert to PIL Image
            img = Image.open(io.BytesIO(response.content)).convert("RGB")

            # A. ADVANCED NSFW CHECK
            safety_results = nsfw_classifier(img)
            scores = {item['label']: item['score'] for item in safety_results}
            score_nsfw = scores.get('nsfw', 0)
            score_sexy = scores.get('sexy', 0)

            if score_sexy > THRESHOLD_SEXY:
                print(f"[SAFETY] BLOCKED SUGGESTIVE IMAGE! Confidence: {score_sexy:.2f}")
                clean_prompt = clean_prompt.replace(url, PLACEHOLDER_IMAGE)
                extracted_context.append("User tried to show suggestive content (Blocked).")
                was_blocked = True
                continue

            if score_nsfw > THRESHOLD_NSFW:
                print(f"[SAFETY] BLOCKED HARDCORE IMAGE! Confidence: {score_nsfw:.2f}")
                clean_prompt = clean_prompt.replace(url, PLACEHOLDER_IMAGE)
                extracted_context.append("User tried to show explicit NSFW content (Blocked).")
                was_blocked = True
                continue

            print(f"[SAFETY] Image passed NSFW check. (NSFW: {score_nsfw:.2f}, Sexy: {score_sexy:.2f})")

            # B. OCR (EASYOCR)
            img_np = np.array(img)
            text_list = ocr_reader.readtext(img_np, detail=0)
            raw_text = " ".join(text_list)
            
            if any(word in raw_text.lower() for word in BAD_WORDS_BLOCKLIST):
                print(f"[SAFETY] BLOCKED IMAGE TEXT! Found blocked word.")
                clean_prompt = clean_prompt.replace(url, PLACEHOLDER_IMAGE)
                was_blocked = True
                continue
            
            # C. IMAGE CAPTIONING (BLIP LARGE)
            caption_result = captioner(img, max_new_tokens=50)
            description = caption_result[0]['generated_text']
            
            print(f"[CONTEXT] Image content: '{description}'")
            if len(raw_text) > 0:
                print(f"[CONTEXT] Image text: '{raw_text}'")
            
            context_str = f"Image shows: '{description}'."
            if len(raw_text) > 2:
                context_str += f" Image contains text: '{raw_text}'."
            
            extracted_context.append(context_str)

        except Exception as e:
            print(f"[SAFETY] Scan error ({e}). Blocking to be safe.")
            clean_prompt = clean_prompt.replace(url, PLACEHOLDER_IMAGE)
            was_blocked = True

    return clean_prompt, " ".join(extracted_context), was_blocked

# --- PROMPT CONSTRUCTION ---

PROMPT_CORE = """
You are an expert Lua scripter for Garry's Mod (GLua). 
You will receive a request from a livestream chat and the current map name. 
The chat is controlling the streamer's playthrough of Half-Life 2 via your generated scripts.
Generate valid GLua code to execute that request immediately.

GROUND RULES:
1. **Server vs Client Architecture:**
   - You are executing in a SERVER environment.
   - For Physics, Health, Entities, Spawning, and Gravity: Write standard code.
   - For **UI, HUD, Screen Effects, or Client Sounds**: You CANNOT write them directly. You MUST wrap that specific code inside `RunOnClient([[ ... ]])`.
   - *Note:* `LocalPlayer()` is only valid inside the `RunOnClient` wrapper. On the server layer, use `player.GetAll()` or `Entity(1)`.

2. **Temporary Effects:** If the effect is disruptive (blindness, gravity, speed, spawning enemies, screen overlays), you MUST wrap a reversion in a 'timer.Simple'. 
   - Light effects: Can be permanent. (spawning one or a few props/friendly npcs, changing walk speed slightly, chat messages)
   - Mild effects: 15 seconds to 1 minute.
   - Heavy/Chaos effects: 5-10 seconds.

3. **Anti-Softlock:** NEVER use 'entity:Remove()' on key story objects, NPCs, or generic searches (like looping through all ents). Instead, use 'SetNoDraw(true)' and 'SetCollisionGroup(COLLISION_GROUP_IN_VEHICLE)' to hide them, then revert it in the timer.

4. **Safety:** Do not use 'os.execute', 'http.Fetch' (outbound), or file system writes. Do not crash the server, but feel free to temporarily lag it or spawn many entities (limit to 100) for comedic effect.

5. **Humor:** If a request is malicious (e.g., "Dox the streamer"), do a fake version. You can be really relaxed about the rules if the intent is comedic.
   - Example: RunOnClient([=[ chat.AddText(Color(255,0,0), "217.201.21.8") ]=])

6. **POV Awareness:** Try to make sure things happen where the player can see them (unless otherwise stated for comedic effect). For example, spawning something in front of the player rather than behind them or at world origin.

7. DONT CHANGE THE LEVEL FFS PLEASE

8. You can do advanced UI in HTML, for better effects and fancy styling. and js

9. make sure you can interact with UI elements and popups that require it! (MakePopup())

10. keep an eye on FPS for laggy effects, stop and remove until framerate is stable again.
"""

PROMPT_IMAGES = """
7. **Image Context Awareness:**
   - If you receive [SYSTEM DETECTED IMAGE CONTEXT], it means the user linked an image containing specific text or objects.
   - Use this context creatively! If the image context says "Image shows: a spooky ghost", spawn a zombie or play a scream sound.
   - If the user simply asks to "Show this image", pass the URL provided in the prompt to a HTML Panel inside `RunOnClient`.
   - Images have been pre-scanned for safety by a small and somewhat inaccurate AI. use caution if the context seems suspicious.
"""

PROMPT_FOOTER = """
8. **Output:** RETURN ONLY THE RAW LUA CODE. Do not include markdown backticks (```lua) or explanations.

--- EXAMPLES ---

INPUT: "Make everyone tiny"
OUTPUT:
for _, v in pairs(player.GetAll()) do 
    v:SetModelScale(0.2, 1) 
end
timer.Simple(10, function()
    for _, v in pairs(player.GetAll()) do 
        v:SetModelScale(1, 1) 
    end
end)

INPUT: "Disable gravity"
OUTPUT:
RunConsoleCommand("sv_gravity", "0")
timer.Simple(10, function() RunConsoleCommand("sv_gravity", "600") end)

INPUT: "Make the screen go black for 5 seconds"
OUTPUT:
RunOnClient([=[
    local black = vgui.Create("DPanel")
    black:SetSize(ScrW(), ScrH())
    black:SetBackgroundColor(Color(0,0,0))
    black:Center()
    timer.Simple(5, function() if IsValid(black) then black:Remove() end end)
]=])

INPUT: "Show this image: https://imgur.com/cat.jpg"
CONTEXT: "Image shows: a cute cat."
OUTPUT:
-- We use [=[ and ]=] for the wrapper. We do NOT use MakePopup() as that takes control away.
RunOnClient([=[
    local html = vgui.Create("DHTML")
    html:SetSize(500, 500)
    html:Center()
    -- Using inline CSS to make image fit perfectly
    html:SetHTML([[ <body style="margin:0;overflow:hidden"><img src="https://imgur.com/cat.jpg" style="width:100%;height:100%;object-fit:contain"></body> ]])
    
    surface.PlaySound("garrysmod/content_downloaded.wav")
    
    timer.Simple(10, function() if IsValid(html) then html:Remove() end end)
]=])

INPUT: "Show a message box that types out 'The chat has taken control of your destiny...'"
OUTPUT:
RunOnClient([=[
    local text = "* The chat has taken control of your destiny..."
    local currentChar = 0
    local typingSound = "buttons/button17.wav"
    
    local box = vgui.Create("DPanel")
    box:SetSize(600, 100)
    box:SetPos((ScrW() - 600) / 2, ScrH() - 150)
    
    box.Paint = function(self, w, h)
        -- Black box with white border
        surface.SetDrawColor(0, 0, 0, 250)
        surface.DrawRect(0, 0, w, h)
        surface.SetDrawColor(255, 255, 255, 255)
        surface.DrawOutlinedRect(0, 0, w, h, 2)
    end
    
    local textLabel = vgui.Create("DLabel", box)
    textLabel:SetFont("Trebuchet24")
    textLabel:SetTextColor(Color(255, 255, 255))
    textLabel:SetPos(20, 20)
    textLabel:SetSize(560, 60)
    textLabel:SetText("")
    
    -- Type out the text character by character
    local function TypeText()
        if currentChar < #text then
            currentChar = currentChar + 1
            textLabel:SetText(string.sub(text, 1, currentChar))
            surface.PlaySound(typingSound)
            timer.Simple(0.05, TypeText)
        end
    end
    
    TypeText()
    
    -- Remove after 8 seconds
    timer.Simple(8, function()
        if IsValid(box) then box:Remove() end
    end)
]=])
"""

# Construct the final system prompt based on config
if ENABLE_IMAGES:
    SYSTEM_PROMPT = PROMPT_CORE + PROMPT_IMAGES + PROMPT_FOOTER 
else:
    SYSTEM_PROMPT = PROMPT_CORE + PROMPT_FOOTER


def generate_lua(user_request, current_map, image_context):
    full_user_content = f"Current Map: {current_map}. Request: {user_request}"
    
    if ENABLE_IMAGES and image_context:
        full_user_content += f"\n[SYSTEM DETECTED IMAGE CONTEXT]: {image_context}"

    try:
        completion = client.chat.completions.create(
            #model="anthropic/claude-3.5-sonnet", 
            model="anthropic/claude-sonnet-4.5",
            messages=[
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": full_user_content}
            ]
        )
        code = completion.choices[0].message.content.replace("```lua", "").replace("```", "").strip()
        return code
    except Exception as e:
        print(f"AI Error: {e}")
        return 'print("AI Generation Failed")'

@app.route('/')
def index():
    return render_template_string(HTML_TEMPLATE)

@app.route('/poll', methods=['POST'])
def poll():
    # Use force=True to avoid 415 errors
    data = request.get_json(force=True, silent=True) or {}
    if command_queue:
        return jsonify({"has_code": True, "code": command_queue.pop(0)})
    return jsonify({"has_code": False})

@app.route('/trigger', methods=['POST'])
def trigger():
    data = request.get_json(force=True, silent=True) or {}
    user_request = data.get('prompt')
    if not user_request: return jsonify({"error": "No prompt"}), 400

    # 1. Sanitize & Scan (Blocks bad images, extracts text/context)
    # If ENABLE_IMAGES is false, this returns immediately with empty context
    safe_request, image_context, was_blocked = sanitize_and_scan(user_request)

    if was_blocked:
        print(f"[SAFETY] Ignoring Request: {user_request}")
        return jsonify({
            "status": "ignored", 
            "message": "Safety protocols blocked this request."
        })
    
    current_map = "unknown" 
    
    print(f"Generating code for: {safe_request}")
    if image_context: print(f"Context: {image_context}")

    # 2. Generate Code
    generated_code = generate_lua(safe_request, current_map, image_context)
    command_queue.append(generated_code)
    
    return jsonify({
        "status": "queued", 
        "code_preview": generated_code,
        "context_found": image_context,
        "was_blocked": was_blocked
    })

@app.route('/scan_test', methods=['POST'])
def scan_test():
    """
    Endpoint to manually test if an image is considered safe and what the AI sees.
    """
    if not ENABLE_IMAGES:
        return jsonify({"error": "Image processing is disabled in configuration."}), 400

    data = request.get_json(force=True, silent=True) or {}
    url_to_test = data.get('url')
    
    if not url_to_test:
        return jsonify({"error": "No url provided"}), 400

    print(f"--- TESTING IMAGE: {url_to_test} ---")
    prompt_dummy = f"Test scan {url_to_test}"
    clean_prompt, context, was_blocked = sanitize_and_scan(prompt_dummy)
    
    return jsonify({
        "original_url": url_to_test,
        "blocked": was_blocked,
        "replaced_url": PLACEHOLDER_IMAGE if was_blocked else url_to_test,
        "ai_context_extracted": context
    })

if __name__ == '__main__':
    print(f"Brain is running on port 5000... (Images Enabled: {ENABLE_IMAGES})")
    print(f"Open http://127.0.0.1:5000/ in your browser to test!")
    app.run(port=5000)