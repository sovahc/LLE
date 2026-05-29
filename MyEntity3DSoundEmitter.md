# MyEntity3DSoundEmitter

## Пространство имён
```csharp
using Sandbox.Game.Entities;
```

## Конструкторы
```csharp
// Привязка к сущности (позиция/скорость берутся из entity автоматически)
new MyEntity3DSoundEmitter(MyEntity entity, bool? owner = null)

// Без сущности — позицию задаёшь вручную через SetPosition()
new MyEntity3DSoundEmitter(null)
```

## Основные методы

| Метод | Назначение |
|---|---|
| `PlaySound(MySoundPair, stopPrevious, skipIntro, force2D, alwaysHearOnRealistic, skipToEnd)` | Воспроизвести звук (основной метод) |
| `PlaySingleSound(MySoundPair, bool update)` | Однократный звук (без лупа) |
| `PlayIntroLoopPair(introCue, loopCue)` | Ввод + зацикленный звук |
| `StopSound(bool forced)` | Остановить |
| `SetPosition(Vector3)` | Установить позицию (если entity == null) |
| `SetVelocity(Vector3)` | Установить скорость (для доплера) |
| `Update()` | Обновить позицию/состояние (вызывать периодически) |
| `Cleanup()` | Освободить ресурсы (обязательно при удалении) |

## Свойства

| Свойство | Назначение |
|---|---|
| `IsPlaying` | Играет ли сейчас звук |
| `CustomVolume` | Громкость (умножитель) |
| `CustomMaxDistance` | Макс. дистанция слышимости |
| `VolumeMultiplier` | Ещё один умножитель громкости |
| `Entity` | Привязанная сущность (можно менять на лету) |
| `EmitterMethods` | Словарь делегатов для кастомизации поведения (CanHear, ShouldPlay2D, CueType, ImplicitEffect) |

---

## Паттерн 1: Однократный звук в точке (взрыв, клик)

*Источник: `ExplosionEffect.cs` (JumpExplode)*

```csharp
var emitter = new MyEntity3DSoundEmitter(null);
emitter.SetPosition(center);
emitter.SetVelocity(Vector3.Zero);
emitter.CustomMaxDistance = (float)Math.Pow(50, 2);
emitter.CustomVolume = 2f;
emitter.PlaySingleSound(new MySoundPair("ArcWepLrgWarheadExpl"), true);
// emitter сам очистится когда звук закончится
```

## Паттерн 2: Звук, привязанный к блоку (луп)

*Источник: `NaniteConstructionBlock.cs`*

```csharp
// Инициализация
m_soundPair = new MySoundPair("ArcParticleElectrical");
m_soundEmitter = new MyEntity3DSoundEmitter((MyEntity)m_constructionBlock);
m_soundEmitter.CustomMaxDistance = 30f;
m_soundEmitter.CustomVolume = 2f;

// Включить
m_soundEmitter.PlaySound(m_soundPair, true);

// Выключить
m_soundEmitter.StopSound(true);

// При уничтожении
m_soundEmitter.StopSound(true);
```

## Паттерн 3: HUD-звук (2D, всегда слышен)

*Источник: `HUDSounds.cs` (BuildInfo)*

```csharp
SoundEmitter = new MyEntity3DSoundEmitter(null);

// Убрать все встроенные условия и эффекты
SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CanHear].ClearImmediate();
SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ShouldPlay2D].ClearImmediate();
SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CueType].ClearImmediate();
SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ImplicitEffect].ClearImmediate();

SoundEmitter.SetPosition(MyAPIGateway.Session.Camera.WorldMatrix.Translation);
SoundEmitter.CustomVolume = volume * VolumeMultiplier;
SoundEmitter.PlaySound(soundPair, stopPrevious: false, alwaysHearOnRealistic: true, force2D: true);

// При разгрузке
SoundEmitter.Cleanup();
```

## Паттерн 4: Динамический звук с обновлением позиции (спрей, вода)

*Источник: `SpraySoundEmitter.cs` (PaintGun)*

```csharp
SoundEmitter = new MyEntity3DSoundEmitter(null);

// В Update():
SoundEmitter.SetPosition(PositionGetter.Invoke());
SoundEmitter.CustomVolume = soundVolume;
if (!SoundEmitter.IsPlaying)
    SoundEmitter.PlaySound(spraySound, stopPrevious: true, skipIntro: true, force2D: false);

// Остановка
SoundEmitter.StopSound(false);

// При удалении
SoundEmitter.Cleanup();
```

## Паттерн 5: Смена Entity на лету

*Источник: `NaniteLifeSupportTargets.cs`*

```csharp
m_progressSoundEmitter = new MyEntity3DSoundEmitter((MyEntity)constructionBlock.ConstructionBlock);
// ...
m_progressSoundEmitter.Entity = (MyEntity)player.Controller.ControlledEntity.Entity;
m_progressSoundEmitter.PlaySound(m_progressSound, true, true);
```

## Паттерн 6: Окружающие звуки (несколько эмиттеров)

*Источник: `WaterSoundComponent.cs`*

```csharp
// Несколько эмиттеров для разных типов звуков
private MyEntity3DSoundEmitter _ambientSoundEmitter = new MyEntity3DSoundEmitter(null);
private MyEntity3DSoundEmitter _environmentUnderwaterSoundEmitter = new MyEntity3DSoundEmitter(null);

// Включить
if (!_ambientSoundEmitter.IsPlaying)
{
    _ambientSoundEmitter.PlaySound(WaterData.AmbientSound);
    _ambientSoundEmitter.SetPosition(cameraPosition + randomOffset);
    _ambientSoundEmitter.VolumeMultiplier = volumeMultiplier;
}

// Остановить все при разгрузке
_ambientSoundEmitter.Cleanup();
```

---

## Ключевые моменты

1. **`MySoundPair`** — создаётся по строке-имени кюэ: `new MySoundPair("ArcWepLrgWarheadExpl")`
2. **`PlaySound` vs `PlaySingleSound`** — первый поддерживает лупы и интро, второй — однократный
3. **`Cleanup()`** — обязательно вызывать при удалении компонента/эффекта
4. **`SetPosition()`** — нужен когда entity == null, иначе позиция берётся из `entity.WorldMatrix.Translation`
5. **`EmitterMethods`** — словарь делегатов для тонкой настройки (CanHear, ShouldPlay2D и т.д.), можно очищать/добавлять свои
6. **`force2D: true`** — звук не затухает с расстоянием (HUD, UI)
7. **`alwaysHearOnRealistic: true`** — игнорирует RealisticSound настройки
8. **`stopPrevious: true`** — останавливает предыдущий звук на этом эмиттере перед воспроизведением нового
