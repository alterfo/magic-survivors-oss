using System.Collections.Generic;
using UnityEngine;

// Артефакты из сундуков: постоянные бонусы на весь забег. Уникальные выпадают один раз.
public static class Artifacts
{
    static readonly Color Common = new(0.8f, 0.8f, 0.85f), Rare = new(0.4f, 0.7f, 1f), Epic = new(1f, 0.75f, 0.25f);

    public static List<Artifact> All() => new()
    {
        new() { title = "Кровавый грааль", icon = "a_grail", desc = "Вампиризм +3%", color = Rare, apply = g => g.player.vamp += 0.03f },
        new() { title = "Перо феникса", icon = "a_phoenix", desc = "Один раз воскрешает с половиной здоровья", color = Epic, unique = true, apply = g => g.player.revives++ },
        new() { title = "Песочные часы", icon = "a_hourglass", desc = "Заклинания на 10% быстрее", color = Rare, apply = g => g.player.cdMul *= 0.9f },
        new() { title = "Око бури", icon = "a_stormeye", desc = "Шанс крита +10%", color = Rare, apply = g => g.player.crit += 0.1f },
        new() { title = "Сапоги ветра", icon = "a_windboots", desc = "Скорость бега +15%", color = Common, apply = g => g.player.speed *= 1.15f },
        new() { title = "Магнитный камень", icon = "a_lodestone", desc = "Радиус сбора опыта +50%", color = Common, apply = g => g.player.magnet *= 1.5f },
        new() { title = "Каменная кожа", icon = "a_stoneskin", desc = "Входящий урон −12%", color = Rare, apply = g => g.player.armor += 0.12f },
        new() { title = "Корона лича", icon = "a_lichcrown", desc = "Урон всех заклинаний +20%", color = Epic, apply = g => g.player.dmgMul += 0.2f },
        new() { title = "Сердце голема", icon = "a_golemheart", desc = "+50 к здоровью и полное лечение", color = Common, apply = g => { g.player.maxHp += 50; g.player.hp = g.player.maxHp; } },
        new() { title = "Четырёхлистник", icon = "a_clover", desc = "При повышении уровня на выбор 4 карты", color = Epic, unique = true, apply = g => g.player.extraChoice++ },
        new() { title = "Шипастый доспех", icon = "a_spikearmor", desc = "Скелеты получают 150% урона, который наносят", color = Rare, apply = g => g.player.thorns += 1.5f },
        new() { title = "Книга мудреца", icon = "a_book", desc = "Опыт +25%", color = Common, apply = g => g.player.xpMul += 0.25f },
        new() { title = "Линза мага", icon = "a_lens", desc = "Площадь заклинаний +20%", color = Rare, apply = g => g.player.area += 0.2f },
        new() { title = "Плащ тени", icon = "a_cloak", desc = "Скелеты, коснувшиеся мага, замедлены", color = Rare, unique = true, apply = g => g.player.cloak = true },
        new() { title = "Глаз ворона", icon = "a_raveneye", desc = "Критический урон сильнее на 0.5", color = Rare, unique = true, apply = g => g.player.critMul += 0.5f },
        new() { title = "Череп некроманта", icon = "a_necroskull", desc = "+1% урона за каждые 100 убийств, до +30%", color = Epic, unique = true, apply = g => g.player.necro = 1 },
        new() { title = "Кровавый камень", icon = "a_bloodstone", desc = "Каждое убийство лечит на 1", color = Common, apply = g => g.player.killHeal += 1 },
        new() { title = "Компас искателя", icon = "a_compass", desc = "Сундуки и реликвии появляются в 1.5 раза чаще", color = Common, unique = true, apply = g => g.player.findMul *= 1.5f },
        new() { title = "Сердце дракона", icon = "a_dragonheart", desc = "Регенерация +1.5 здоровья в секунду", color = Epic, apply = g => g.player.regen += 1.5f },
    };
}
