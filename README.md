**No Decay** nullifies or scales down/up any decay damage applied to any item in game.

## Configuration

```json
{
  "General": {
    "Disable decay for all entities": false,
    "Exclude \"Other Multipliers\"": false,
    "Use permission": false,
    "Permission": "nodecay.use",
    "Output": {
      "Output to server console": false,
      "Log to file": false,
      "Log file name": "NoDecay-Log"
    },
    "Cupboard Settings": {
      "Disable No Decay if resources placed in TC": false,
      "Require Tool Cupboard": true,
      "Any authed on TC": false,
      "Cupboard Range": 30.0,
      "Block cupboard wood": false,
      "Block cupboard stone": false,
      "Block cupbard metal": false,
      "Block cupboard high quality": false
    }
  },
  "Building grade multipliers": {
    "Twig multiplier": 1.0,
    "Wood multiplier": 0.0,
    "Stone multiplier": 0.0,
    "Sheet Metal multiplier": 0.0,
    "Armoured multiplier": 0.0
  },
  "Other multipliers": {
    "shortname": 0.0
  },
  "Use permission": "nodecay.use"
}
```
 
 ### General
 
*  **Disable decay for all entities** - Is decay damage disabled for all entities.
*  **Exclude \"Other Multipliers\"** - Will make all entities entered in Other Multipliers exempt from the plugins effects (will decay).
*  **Use permission** - Do players require the permission for decay multipliers to be applied to their owned entities.
*  **Decay if there is no owner** - If permissions are enabled, do entities without an owner decay as normal or not.
*  **Output to server console** - Does the plugin output information about its operation to the server console.
*  **Log to file** - Does the plugin output information about its operation to a data file.
*  **Log file name** - The file name that the plugin outputs to.

### Cupboard Settings

* **Disable No Decay if resources placed in TC** - Will disable the effects of the plugin on entities under the building priviledge range if resources are in the tool cupboard.
*  **Require Tool Cupboard** - Is the entity required to be in range of a tool cupboard for decay multipliers to be applied.
*  **Any authed on TC** - If any of the players authorised on the tool cupboard have the permission, decay will be overriden.
*  **Cupboard Range** - The max distance between the center of the tool cupboard and the entity to be classed as in range.
*  **Block cupboard wood** - Is wood blocked from being added to tool cupboards.
*  **Block cupboard stone** - Is stone blocked from being added to tool cupboards.
*  **Block cupboard metal** - Are metal fragments blocked from being added to tool cupboards.
*  **Block cupboard high quality** - Is high quality metal blocked from being added to tool cupboards.


### Building grade multipliers

These are the values that the decay damage is scaled to. 1.0 will result in no change in decay, 0.0 will remove it completely. Going above 1.0 will increase decay damage.


### Other multipliers

The shortnames of the entities desired to be affected by this plugin must be entered in here.

An example might be:

```json
"Other multipliers": {
    "furnace": 0.0,
    "wall.external.high.stone": 0.5,
    "wall.external.high.stone": 0.5,
    "minicopter.entity": 0.0
},
```

Alternatively, the entity type may be entered.

```json
"Other multipliers": {
    "BaseOven": 0.0,
    "StorageContainer": 0.5,
    "wall.external.high.stone": 0.5,
    "minicopter.entity": 0.0
},
```


The multiplier for the individual entity will override the type multiplier. For example if the config looked like this:

```json
"Other multipliers": {
    "BaseOven": 0.0,
    "furnace": 0.5
},
```

The multiplier used for the furnace would be 0.5 .
 
## Permissions

* `nodecay.use` - Required for NoDecay to work for a user, if the permission is required (set in config).

## Notes

Decay is implemented by Rust based on the decay.tick value which defaults to 600 (10 minutes).  This specifies how often decay is processed on your server.

The warning from Rust about Building Decaying cannot be bypassed at this time except by stocking a TC with the appropriate materials.  If a player adds materials to his TC, NoDecay will be bypassed altogether for their building, and normal upkeep costs will apply.  There are server variables available to adjust cost and decay rates, but that is outside of the scope of what NoDecay is intended to do.

## Credits
* **Deicide666ra** and **Piarb**, the original authors of the (1.0.26 - 1.0.40) plugin.
* **Diesel_42o** and **rfc1920**, for helping maintain the (1.0.26 - 1.0.40) plugin.
* **Zoin**, for helping maintain the (2.0.0 - 2.0.2) plugin.
* **[WhiteThunder](https://umod.org/user/WhiteThunder)**, for helping identify code improvements.
