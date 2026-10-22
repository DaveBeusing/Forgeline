"""Conveyor visual contract tests run in standard PR Python CI (no Blender)."""
import copy
import sys
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from conveyor_contract import (  # noqa: E402
    DEFAULT_BOARD,
    DEFAULT_KIT,
    can_mate,
    module_by_slug,
    read_kit,
    validate,
    validate_board,
)


class ConveyorContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.kit = read_kit()

    def ports(self, slug):
        return module_by_slug(self.kit, slug)["ports"]

    def test_complete_twelve_module_catalog_and_measured_cross_section(self):
        validate(self.kit)
        self.assertEqual(12, len(self.kit["modules"]))
        self.assertEqual(5.2, self.kit["geometry"]["assemblyUndersideYTierZeroMeters"])
        self.assertEqual(5, self.kit["geometry"]["roadPassageMinimumMeters"])
        self.assertEqual(9, len(module_by_slug(self.kit, "conveyor_merger_2to1")["variants"]))
        self.assertEqual(4, len(module_by_slug(self.kit, "conveyor_support_heavy")["variants"]))

    def test_straights_mate_without_a_gap_or_a_direction_mismatch(self):
        short_out = self.ports("conveyor_straight_short")[1]
        standard_in = self.ports("conveyor_straight_standard")[0]
        self.assertTrue(can_mate(short_out, standard_in,
                                 out_origin=(0, 0, -6),
                                 in_origin=(0, 0, 0)))
        self.assertFalse(can_mate(short_out, standard_in,
                                  out_origin=(0, 0, -5.95),
                                  in_origin=(0, 0, 0)))
        self.assertFalse(can_mate(short_out, standard_in,
                                  out_origin=(0, 0, -6),
                                  in_origin=(0, 0, 0),
                                  in_yaw=90))

    def test_ninety_degree_curve_closes_the_two_cardinal_straights(self):
        straight = self.ports("conveyor_straight_standard")
        curve = self.ports("conveyor_curve_90")
        self.assertTrue(can_mate(straight[1], curve[0],
                                 out_origin=(-4, 0, -4),
                                 in_origin=(0, 0, 0)))
        self.assertTrue(can_mate(curve[1], straight[0],
                                 out_origin=(0, 0, 0),
                                 in_origin=(4, 0, 4),
                                 in_yaw=90))
        self.assertFalse(can_mate(curve[1], straight[0],
                                  out_origin=(0, 0, 0),
                                  in_origin=(4, 0, 4),
                                  in_yaw=180))

    def test_ramps_link_real_height_tiers_not_flat_projections(self):
        straight = self.ports("conveyor_straight_standard")
        ramp = self.ports("conveyor_ramp_up")
        self.assertTrue(can_mate(straight[1], ramp[0],
                                 out_origin=(0, 0, -8), in_origin=(0, 0, 0)))
        self.assertTrue(can_mate(ramp[1], straight[0],
                                 out_origin=(0, 0, 0), in_origin=(0, 2, 8)))
        self.assertFalse(can_mate(ramp[1], straight[0],
                                  out_origin=(0, 0, 0), in_origin=(0, 0, 8)))
        reverse = self.ports("conveyor_ramp_down")
        self.assertEqual(1, reverse[0]["relativeTier"])
        self.assertEqual(0, reverse[1]["relativeTier"])

    def test_merger_and_splitter_ports_reach_both_input_lanes(self):
        straight = self.ports("conveyor_straight_standard")
        merger = self.ports("conveyor_merger_2to1")
        splitter = self.ports("conveyor_splitter_1to2")
        for lane, inlet in ((-4, merger[0]), (4, merger[1])):
            self.assertTrue(can_mate(straight[1], inlet,
                                     out_origin=(lane, 0, -8),
                                     in_origin=(0, 0, 0)))
        self.assertTrue(can_mate(merger[2], straight[0],
                                 out_origin=(0, 0, 0), in_origin=(0, 0, 8)))
        self.assertTrue(can_mate(straight[1], splitter[0],
                                 out_origin=(0, 0, -8), in_origin=(0, 0, 0)))
        for lane, outlet in ((-4, splitter[1]), (4, splitter[2])):
            self.assertTrue(can_mate(outlet, straight[0],
                                     out_origin=(0, 0, 0),
                                     in_origin=(lane, 0, 8)))

    def test_adapter_requires_real_building_socket_and_endcap_never_accepts_material(self):
        adapter_input = self.ports("conveyor_io_in")
        adapter_output = self.ports("conveyor_io_out")
        short = self.ports("conveyor_straight_short")
        self.assertEqual("building_socket", adapter_input[0]["role"])
        self.assertEqual("building_socket", adapter_output[1]["role"])
        self.assertFalse(can_mate(adapter_input[1], adapter_output[1]))
        self.assertFalse(can_mate(adapter_output[1], short[0],
                                  out_origin=(0, 0, 0), in_origin=(0, 0, 4)))
        self.assertEqual("cap", self.ports("conveyor_endcap")[0]["kind"])
        self.assertFalse(can_mate(self.ports("conveyor_endcap")[0], short[0]))

    def test_reject_duplicate_ids_liquids_and_nonfitting_clearance(self):
        repeated = copy.deepcopy(self.kit)
        repeated["modules"][1]["assetId"] = repeated["modules"][0]["assetId"]
        with self.assertRaisesRegex(ValueError, "duplicated identity"):
            validate(repeated)
        liquids = copy.deepcopy(self.kit)
        liquids["flow"]["beltResources"].append("fuel")
        with self.assertRaisesRegex(ValueError, "whitelist overlaps fluids"):
            validate(liquids)
        low = copy.deepcopy(self.kit)
        low["geometry"]["assemblyUndersideYTierZeroMeters"] = 4.99
        with self.assertRaisesRegex(ValueError, "road clearance"):
            validate(low)
        inverted = copy.deepcopy(self.kit)
        inverted["modules"][0]["ports"][0]["positionMeters"][1] = 5.1
        with self.assertRaisesRegex(ValueError, "port Y"):
            validate(inverted)

    def test_greybox_svg_is_editable_valid_and_complete(self):
        validate_board(self.kit)
        board = DEFAULT_BOARD.read_text(encoding="utf-8")
        root = ET.fromstring(board)
        self.assertTrue(root.tag.endswith("svg"))
        self.assertIn("5.00 m MIN", board)
        self.assertTrue(DEFAULT_KIT.name.endswith(".v1.json"))
        self.assertFalse(DEFAULT_KIT.name.endswith(".asset.json"))


if __name__ == "__main__":
    unittest.main()
