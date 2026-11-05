// rex/ui/vulkan/device_order.h (patches/rexglue 0047): which GPU automatic selection tries first.

#include <catch_amalgamated.hpp>

#include <vector>

#include <rex/ui/vulkan/device_order.h>

using rex::ui::vulkan::DeviceCandidate;
using rex::ui::vulkan::OrderDeviceCandidates;
using Candidate = DeviceCandidate<int>;

namespace {

std::vector<int> Order(std::vector<Candidate> candidates) {
  OrderDeviceCandidates(candidates);
  std::vector<int> devices;
  for (const Candidate& candidate : candidates) {
    devices.push_back(candidate.device);
  }
  return devices;
}

}  // namespace

TEST_CASE("a discrete GPU comes before an equally capable integrated one listed first") {
  CHECK(Order({{0, 4, VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU},
               {1, 4, VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU}}) == std::vector<int>{1, 0});
}

TEST_CASE("a software device comes last among equally capable devices") {
  CHECK(Order({{0, 4, VK_PHYSICAL_DEVICE_TYPE_CPU},
               {1, 4, VK_PHYSICAL_DEVICE_TYPE_OTHER},
               {2, 4, VK_PHYSICAL_DEVICE_TYPE_VIRTUAL_GPU},
               {3, 4, VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU}}) == std::vector<int>{3, 2, 1, 0});
}

TEST_CASE("features still decide before the device type") {
  CHECK(Order({{0, 3, VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU},
               {1, 4, VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU}}) == std::vector<int>{1, 0});
}

TEST_CASE("complete ties keep the enumeration order") {
  CHECK(Order({{0, 4, VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU},
               {1, 4, VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU}}) == std::vector<int>{0, 1});
}
