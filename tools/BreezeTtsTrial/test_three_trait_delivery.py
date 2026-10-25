import unittest
import coordinator


class ThreeTraitDelivery(unittest.TestCase):
    def context(self):
        return dict(schemaVersion=2, personId="npc:three", mood=0, dispositionKnown=True,
                    disposition=20, trustKnown=False, trust=0, fear=0, resentment=0,
                    encounters=1, traits={"greed": "greedy", "generosity": "stingy", "warmth": "unfriendly"})

    def test_three_traits_are_preserved_without_filling_missing_axes(self):
        source = self.context()
        result = coordinator.parse_delivery_context(source, "npc:three")
        self.assertEqual(result, source)
        source["traits"]["greed"] = "unmaterialistic"
        self.assertEqual(result["traits"]["greed"], "greedy")

    def test_wrong_count_and_unknown_values_are_rejected(self):
        for traits in ({"greed": "greedy"}, {"greed": "greedy", "generosity": "stingy", "warmth": "invented"}):
            context = self.context(); context["traits"] = traits
            with self.assertRaises(coordinator.RequestError):
                coordinator.parse_delivery_context(context, "npc:three")

    def test_sparse_personality_does_not_require_missing_voice_axes(self):
        context = self.context()
        context["fear"] = 80
        parsed = coordinator.parse_delivery_context(context, "npc:three")
        baseline, current = coordinator.personality_directions(parsed)
        self.assertIn("unfriendly", baseline)
        self.assertTrue(current)
        context["traits"] = {"greed": "greedy", "generosity": "stingy", "integrity": "deceitful"}
        baseline, current = coordinator.personality_directions(coordinator.parse_delivery_context(context, "npc:three"))
        self.assertIn("no supplied prosody trait", baseline)


if __name__ == "__main__":
    unittest.main()
