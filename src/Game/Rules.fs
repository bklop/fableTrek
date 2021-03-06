module Game.Rules

open Types
open Utils 

module Enemy =
  let inline private overflowSubtract (current:^T) (value:^T) =
    let zero = LanguagePrimitives.GenericZero<'T>
    let newValue = current - value
    if newValue < zero then
      zero, abs newValue      
    else
      newValue, zero

  let energyImpact gameObject energy =    
    match gameObject.Attributes with
    | EnemyAttributes enemy ->
      let newShields, shieldsOverflow = overflowSubtract enemy.Shields.Current energy
      let newHull, _ = overflowSubtract enemy.HitPoints.Current ((shieldsOverflow |> float) * 1.<hitpoints>)
      if newHull <= 0.<hitpoints> then
        None
      else
        Some { gameObject with Attributes = { enemy with Shields = enemy.Shields.Update newShields
                                                         HitPoints = enemy.HitPoints.Update newHull
                                            } |> EnemyAttributes
             }      
    | _ -> Some gameObject

module Damage =
  let efficiencyFactor (systemHitPoints:HitPoints) =
    let percentage = systemHitPoints.Percentage
    let efficiency = System.Math.Log(percentage * 10., 10.)
    efficiency

  let inefficiencyFactor (systemHitPoints:HitPoints) =
    let percentage = systemHitPoints.Percentage
    let inefficiency = 1.+(1.-System.Math.Log(percentage * 10., 10.))
    inefficiency

  let systemRepairTime (player:Player) (systemHitPoints:HitPoints) =
    let hitPointsNeedingRepair = systemHitPoints.Max - systemHitPoints.Current
    hitPointsNeedingRepair / player.HitPointsRepairedPerDay

  let totalRepairTime (player:Player) =
    (player.SystemsAsList |> Seq.sumBy (fun (_,hp) -> hp.Max - hp.Current)) / player.HitPointsRepairedPerDay

  let canRepair (game:Game) =
    (
      game
      |> Game.Utils.GameWorld.currentSectorObjects
      |> Seq.tryFind (fun go -> go.IsEnemy)
      |> Option.isNone
    ) && (game.Player |> totalRepairTime) > 0.
    

module Weapons =
  let canAddTarget (player:Player) (gameObject:GameObject) =
    (player.Targets |> List.tryFind (fun t -> t = gameObject.Position)) |> Option.isNone &&
    player.Targets.Length < 3 &&
    gameObject.IsEnemy

  let addTarget (player:Player) (gameObject:GameObject) =
    if canAddTarget player gameObject  then
      Ok { player with Targets = [gameObject.Position] |> List.append player.Targets }
    else
      Error (sprintf "Cannot target object at %s" gameObject.Position.AsString)

  let removeTarget (player:Player) position =
    let newPlayer = { player with Targets = player.Targets |> List.filter (fun p -> p <> position)}
    if newPlayer.Targets.Length = player.Targets.Length then
      Error "No target to remove"
    else
      Ok newPlayer

  let canFirePhasers player =
    player.Targets.Length > 0 &&
    (player.PhaserPower.Current * (player.Targets.Length |> float) <= player.Energy.Current)

  let canFireTorpedoes player =
    player.Targets.Length > 0 && player.Targets.Length < (player.Torpedos.Current |> int)

  let calculatePhaserPower player =
    // TODO: factor distance to targer in here
    (player.Phasers |> Damage.efficiencyFactor) * player.PhaserPower.Current

  let calculatePhaserTemperatureIncrease player =
    if player.Phasers.Current <= 0.<hitpoints> then
      player.PhaserTemperature.Max
    else
      let damageModifier = 1. + (1. - System.Math.Log(player.Phasers.Percentage * 1000., 1000.))
      (player.PhaserPower.Current |> float) * player.PhaserTemperatureCostPerGigawatt * damageModifier        

  let calculatePhaserDamage player =
    0.<hitpoints>
  
  let inline firePhasers game targetPosition =
    let optionalTarget = Game.Utils.GameWorld.objectAtPosition game.GameObjects targetPosition
    match optionalTarget with
    | Some target ->
      let phaserEnergyHit = game.Player |> calculatePhaserPower
      let modifiedTarget = phaserEnergyHit |> Enemy.energyImpact target
      let modifiedPlayer = { game.Player with Energy = (game.Player.Energy.Update (game.Player.Energy.Current - game.Player.PhaserPower.Current))
                                              PhaserTemperature = game.Player.PhaserTemperature.Update(game.Player.PhaserTemperature.Current + (game.Player |> calculatePhaserTemperatureIncrease))
                                              Phasers = game.Player.Phasers.Update (game.Player.Phasers.Current + (game.Player |> calculatePhaserDamage))
                           }
      match modifiedTarget with
      | Some damagedTarget ->
        let logMessage = (sprintf "Hit of %.0f gigawatts on %s at %s" phaserEnergyHit target.Name target.Position.SectorPosition.AsString)
        { game with GameObjects = Game.Utils.GameWorld.replaceGameObject game.GameObjects damagedTarget
                    Player = { modifiedPlayer with CaptainsLog = [logMessage |> Information] |> List.append game.Player.CaptainsLog }
        } |> FiringResponse.TargetDamaged
      | None -> // destroyed target
        let logMessage = (sprintf "Destroyed %s at %s" target.Name target.Position.SectorPosition.AsString)
        { game with GameObjects = Game.Utils.GameWorld.removeGameObject game.GameObjects target
                    Player = { modifiedPlayer with CaptainsLog = [logMessage |> Information] |> List.append game.Player.CaptainsLog
                                                   Targets = modifiedPlayer.Targets |> List.filter (fun t -> t <> targetPosition)
                             }
        } |> FiringResponse.TargetDestroyed
    | None ->
      { game with Player = { game.Player with CaptainsLog = ["Phasers missed!" |> Warning] |> List.append game.Player.CaptainsLog }} |> FiringResponse.TargetMissed

module Movement =
  open Damage

  let isImpulseMove (player:Player) newPosition = player.Position.GalacticPosition = newPosition.GalacticPosition

  let netEnergyRequirementsForWarp (player:Player) newPosition = 
    let energyRequirementsForMove (player:Player) newPosition =
      if isImpulseMove player newPosition then
        // we're moving within a sector
        let distance = player.Position.SectorPosition.DistanceTo newPosition.SectorPosition
        let energyCost = player.ImpulseMovementCost * (player.ImpulseDrive |> inefficiencyFactor) * distance
        energyCost
      else
        let distance = player.Position.GalacticPosition.DistanceTo newPosition.GalacticPosition
        let energyCostAtSpeed = player.EnergyCostPerUnitOfTravel * (1.-System.Math.Log((11.-(player.WarpSpeed.Current |> float))/2., 5.5))
        let energyCostAtSpeedAndDamage = energyCostAtSpeed * (inefficiencyFactor player.WarpDrive)
        let totalEnergyCost = distance * energyCostAtSpeedAndDamage      
        Fable.Core.JS.console.log(sprintf "Total energy cost at warp %f: %f" player.WarpSpeed.Current totalEnergyCost)
        totalEnergyCost

    let energyGeneratedByEnergyConverter (player:Player) newPosition =
      if isImpulseMove player newPosition then
        0.<gigawatt>
      else
        let distance = player.Position.GalacticPosition.DistanceTo newPosition.GalacticPosition
        let energyGeneratedAtSpeed = player.EnergyGeneratedPerUnitOfTravel * System.Math.Log(11.-(player.WarpSpeed.Current |> float), 11.)
        let energyGeneratedAtSpeedAndDamage = energyGeneratedAtSpeed * (efficiencyFactor player.EnergyConverter)
        let totalEnergyGenerated = distance * energyGeneratedAtSpeedAndDamage
        Fable.Core.JS.console.log(sprintf "Total energy generated at warp %f: %f" player.WarpSpeed.Current totalEnergyGenerated)
        totalEnergyGenerated
    
    (energyRequirementsForMove player newPosition) - (energyGeneratedByEnergyConverter player newPosition)

  let canMove (player:Player) newPosition =
    let totalEnergyCost = netEnergyRequirementsForWarp player newPosition
    (player.Energy.Current - totalEnergyCost) >= 0.<gigawatt>

  let move player gameObjects newPosition =
    let energyRequirements = netEnergyRequirementsForWarp player newPosition
    if isImpulseMove player newPosition then
      match player.Energy.Current > energyRequirements, (Position.positionIsVacant gameObjects newPosition) with
      | true, true ->
        Ok { player with Energy = player.Energy.Update (player.Energy.Current - energyRequirements) ; Position = newPosition } 
      | false, _ ->
        Error "Insufficient energy to move to that location"
      | _, false ->
        Error "Object blocking move"
    else
      match player.Energy.Current > energyRequirements with
      | true ->
        let confirmedVacantPosition =
          if Game.Utils.Position.positionIsVacant gameObjects newPosition then
            newPosition
          else
            { newPosition with SectorPosition = Game.Utils.Position.findRandomAndVacantSectorPosition gameObjects newPosition.GalacticPosition }
        Ok { player with Energy = player.Energy.Update (player.Energy.Current - energyRequirements) ; Position = confirmedVacantPosition } 
      | false ->
        Error "Insufficient energy to move to that location"

module Sensors =
  let discover (player:Player) alreadyFound =
    // todo - make it a hashset
    let pos = player.Position.GalacticPosition
    let newPositions =
      seq {
        for y = (max (pos.Y-1<coordinatecomponent> |> int) 0) to (min (pos.Y+1<coordinatecomponent> |> int) (GameWorldPosition.Max.GalacticPosition.Y |> int)) do
          for x = (max (pos.X-1<coordinatecomponent> |> int) 0) to (min (pos.X+1<coordinatecomponent> |> int) (GameWorldPosition.Max.GalacticPosition.X |> int)) do
            yield { X = x*1<coordinatecomponent> ; Y = y*1<coordinatecomponent>}
      }
      |> Set.ofSeq
    alreadyFound |> Set.union newPositions


module Turn =
  let generateAiActions game =
    // pretty dumb... just random chance based
    let aiActions =
      game.GameObjects
      |> Seq.collect(fun go ->
        let diceRoll = rollDice ()
        let pgp = game.Player.Position.GalacticPosition
        match go.Attributes,go.Position.GalacticPosition=pgp with
        | EnemyAttributes enemy, true -> // an enemy in the same sector as the player
          if diceRoll < 10 && enemy.Shields.Percentage < 0.6 then
            [{ Instruction = AiInstruction.TransferToShields ; GameObject = go }]
          elif diceRoll < 75 then
            [{Instruction = 100.<gigawatt> |> AiInstruction.FirePhasersAtPlayer ; GameObject = go }]
          elif diceRoll < 95 then
            // todo - nice touch would be to move to the players weakest shield
            let newPosition = Position.findRandomAndVacantSectorPosition game.GameObjects go.Position.GalacticPosition
            [{Instruction = { game.Player.Position with SectorPosition = newPosition} |> AiInstruction.ImpulseMoveTo ; GameObject = go }]
          else
            []
        | EnemyAttributes enemy, false -> // an enemy in a different sector to the player
          if diceRoll < 2 then
            let newPosition = { GalacticPosition = pgp ; SectorPosition = Position.findRandomAndVacantSectorPosition game.GameObjects pgp }
            [{Instruction = newPosition |> AiInstruction.WarpMoveTo  ; GameObject = go }]
          else
            []
        | _,_ -> []
      )
      |> Seq.toList

    { game with AiActions = aiActions }

 