-- ai_chaos_controller.lua

if SERVER then
    util.AddNetworkString("AI_RunClientCode")

    local SERVER_URL = "https://voluntarily-paterfamiliar-jeanie.ngrok-free.dev/poll"
    local POLL_INTERVAL = 2 -- Seconds to wait between requests

    print("[AI Chaos] Server Initialized!")

    -- 1. Helper Function: Send code to client
    function RunOnClient(codeString)
        net.Start("AI_RunClientCode")
        net.WriteString(codeString)
        net.Broadcast()
    end

    -- 2. Helper Function: Run the code safely
    local function ExecuteAICode(code)
        print("[AI Chaos] Running generated code...")
        local success, err = pcall(function()
            -- Print whole code for debugging
            print("[AI Chaos] Executing code:\n" .. code)
            RunString(code)
        end)

        if success then
            --PrintMessage(HUD_PRINTTALK, "[AI] Event triggered!")
        else
            PrintMessage(HUD_PRINTTALK, "[AI] Code Error: " .. tostring(err))
            print("[AI Error]", err)
        end
    end

    -- Forward declaration
    local PollServer 

    -- 3. The Polling Logic
    PollServer = function()
        -- print("[AI Chaos] Polling...") -- Uncomment to see spam in console
        
        local body = { map = game.GetMap() }

        HTTP({
            method = "POST",
            url = SERVER_URL,
            body = util.TableToJSON(body),
            headers = { ["Content-Type"] = "application/json" },
            
            -- ON SUCCESS
            success = function(code, body, headers)
                if code == 200 then
                    local data = util.JSONToTable(body)
                    if data and data.has_code then
                        print("[AI Chaos] Received code!")
                        ExecuteAICode(data.code)
                    end
                else
                    print("[AI Chaos] Server Error Code: " .. tostring(code))
                end
                
                -- Schedule the NEXT poll only after this one finishes
                timer.Simple(POLL_INTERVAL, PollServer)
            end,

            -- ON FAILURE (Important: If Python is closed, this runs)
            failed = function(err)
                print("[AI Chaos] Connection Failed: " .. tostring(err))
                -- Schedule the NEXT poll even if this one failed
                timer.Simple(POLL_INTERVAL, PollServer)
            end
        })
    end

    -- Start the loop
    print("[AI Chaos] Starting Polling Loop...")
    PollServer()

else -- CLIENT SIDE CODE
    
    net.Receive("AI_RunClientCode", function()
        local code = net.ReadString()
        local success, err = pcall(function()
            -- print whole code for debugging
            print("[AI Chaos] Running client code:\n" .. code)
            RunString(code)
        end)
        if not success then print("[AI Client Error]", err) end
    end)
end