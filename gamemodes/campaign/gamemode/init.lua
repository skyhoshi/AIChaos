-- Campaign Gamemode - Server Init
AddCSLuaFile("cl_init.lua")
AddCSLuaFile("shared.lua")

include("shared.lua")

function GM:Initialize()
	print("[Campaign] Campaign Gamemode Initialized")
end

-- Player spawn - only remove suit if they haven't picked it up yet
-- function GM:PlayerSpawn(ply)
-- 	self.BaseClass:PlayerSpawn(ply)
	
-- 	-- Only remove suit if player hasn't picked it up yet
-- 	if ply:GetPData("campaign_has_suit", "0") ~= "1" then
-- 		ply:RemoveSuit()
-- 	end
	
-- 	-- Check suit status shortly after spawn and save it
-- 	timer.Simple(0.5, function()
-- 		if IsValid(ply) and ply:IsSuitEquipped() then
-- 			ply:SetPData("campaign_has_suit", "1")
-- 		end
-- 	end)
-- end

-- -- Save suit status when player disconnects or changes level
-- function GM:PlayerDisconnected(ply)
-- 	if ply:IsSuitEquipped() then
-- 		ply:SetPData("campaign_has_suit", "1")
-- 	end
-- end

-- -- Also save on death (in case of level transition)
-- function GM:PostPlayerDeath(ply)
-- 	if ply:IsSuitEquipped() then
-- 		ply:SetPData("campaign_has_suit", "1")
-- 	end
-- end
